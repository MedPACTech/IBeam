using Azure;
using Azure.Data.Tables;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Repositories.AzureTable.Entities;
using IBeam.Identity.Repositories.AzureTable.Options;
using IBeam.Identity.Repositories.AzureTable.Types;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Repositories.AzureTable.Stores;

public sealed class AzureTableApiCredentialStore : IApiCredentialStore
{
    private readonly TableServiceClient _serviceClient;
    private readonly AzureTableIdentityOptions _opts;

    public AzureTableApiCredentialStore(TableServiceClient serviceClient, IOptions<AzureTableIdentityOptions> opts)
    {
        _serviceClient = serviceClient;
        _opts = opts.Value;
    }

    public async Task<ApiCredentialRecord> CreateAsync(ApiCredentialRecord credential, CancellationToken ct = default)
    {
        try
        {
            var entity = Map(credential);
            await Table().AddEntityAsync(entity, ct).ConfigureAwait(false);
            return Map(entity);
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<IReadOnlyList<ApiCredentialRecord>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        try
        {
            var pk = _opts.ApiCredentialsPk(tenantId);
            var list = new List<ApiCredentialRecord>();
            await foreach (var entity in Table().QueryAsync<ApiCredentialEntity>(
                x => x.PartitionKey == pk && !x.IsDeleted,
                cancellationToken: ct))
            {
                list.Add(Map(entity));
            }

            return list
                .OrderByDescending(x => x.CreatedUtc)
                .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<ApiCredentialRecord?> GetAsync(Guid tenantId, Guid credentialId, CancellationToken ct = default)
    {
        try
        {
            var response = await Table()
                .GetEntityIfExistsAsync<ApiCredentialEntity>(
                    _opts.ApiCredentialsPk(tenantId),
                    _opts.ApiCredentialsRk(credentialId),
                    cancellationToken: ct)
                .ConfigureAwait(false);

            return response.HasValue ? Map(response.Value) : null;
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<ApiCredentialRecord> UpdateRolesAsync(
        Guid tenantId,
        Guid credentialId,
        IReadOnlyList<Guid> roleIds,
        IReadOnlyList<string> roleNames,
        CancellationToken ct = default)
    {
        try
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    var entity = await RequireEntityAsync(tenantId, credentialId, ct).ConfigureAwait(false);
                    entity.RoleIdsCsv = JoinGuids(roleIds);
                    entity.RoleNamesCsv = JoinStrings(roleNames);
                    await Table().UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, ct).ConfigureAwait(false);
                    return Map(entity);
                }
                catch (RequestFailedException ex) when (ex.Status == 412 && attempt < 4)
                {
                }
            }

            throw new IdentityProviderException("Failed to update API credential roles due to concurrent updates.");
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<ApiCredentialRecord> RevokeAsync(
        Guid tenantId,
        Guid credentialId,
        Guid? revokedByUserId,
        string? reason,
        CancellationToken ct = default)
    {
        try
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    var entity = await RequireEntityAsync(tenantId, credentialId, ct).ConfigureAwait(false);
                    entity.RevokedUtc ??= DateTimeOffset.UtcNow;
                    entity.RevokedByUserId ??= revokedByUserId?.ToString("D");
                    entity.RevocationReason = string.IsNullOrWhiteSpace(reason) ? entity.RevocationReason : reason.Trim();
                    await Table().UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, ct).ConfigureAwait(false);
                    return Map(entity);
                }
                catch (RequestFailedException ex) when (ex.Status == 412 && attempt < 4)
                {
                }
            }

            throw new IdentityProviderException("Failed to revoke API credential due to concurrent updates.");
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task TouchLastUsedAsync(
        Guid tenantId,
        Guid credentialId,
        DateTimeOffset usedUtc,
        string? ipAddress,
        CancellationToken ct = default)
    {
        try
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    var entity = await RequireEntityAsync(tenantId, credentialId, ct).ConfigureAwait(false);
                    entity.LastUsedUtc = usedUtc;
                    entity.LastUsedIp = string.IsNullOrWhiteSpace(ipAddress) ? entity.LastUsedIp : ipAddress.Trim();
                    await Table().UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, ct).ConfigureAwait(false);
                    return;
                }
                catch (RequestFailedException ex) when (ex.Status == 412 && attempt < 4)
                {
                }
            }

            throw new IdentityProviderException("Failed to update API credential last-used metadata due to concurrent updates.");
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    private async Task<ApiCredentialEntity> RequireEntityAsync(Guid tenantId, Guid credentialId, CancellationToken ct)
    {
        var response = await Table()
            .GetEntityIfExistsAsync<ApiCredentialEntity>(
                _opts.ApiCredentialsPk(tenantId),
                _opts.ApiCredentialsRk(credentialId),
                cancellationToken: ct)
            .ConfigureAwait(false);

        if (!response.HasValue || response.Value.IsDeleted)
            throw new IdentityNotFoundException($"API credential '{credentialId}' was not found.");

        return response.Value;
    }

    private ApiCredentialEntity Map(ApiCredentialRecord credential)
        => new()
        {
            PartitionKey = _opts.ApiCredentialsPk(credential.TenantId),
            RowKey = _opts.ApiCredentialsRk(credential.CredentialId),
            CredentialId = credential.CredentialId.ToString("D"),
            TenantId = credential.TenantId.ToString("D"),
            DisplayName = credential.DisplayName,
            AgentKey = credential.AgentKey,
            KeyPrefix = credential.KeyPrefix,
            SecretHash = credential.SecretHash,
            RoleNamesCsv = JoinStrings(credential.RoleNames),
            RoleIdsCsv = JoinGuids(credential.RoleIds),
            CreatedUtc = credential.CreatedUtc,
            CreatedByUserId = credential.CreatedByUserId?.ToString("D"),
            ExpiresUtc = credential.ExpiresUtc,
            LastUsedUtc = credential.LastUsedUtc,
            LastUsedIp = credential.LastUsedIp,
            RevokedUtc = credential.RevokedUtc,
            RevokedByUserId = credential.RevokedByUserId?.ToString("D"),
            RevocationReason = credential.RevocationReason,
            IsDeleted = credential.IsDeleted
        };

    private static ApiCredentialRecord Map(ApiCredentialEntity entity)
        => new(
            CredentialId: Guid.Parse(entity.CredentialId),
            TenantId: Guid.Parse(entity.TenantId),
            DisplayName: entity.DisplayName,
            AgentKey: entity.AgentKey,
            KeyPrefix: entity.KeyPrefix,
            SecretHash: entity.SecretHash,
            RoleNames: SplitStrings(entity.RoleNamesCsv),
            RoleIds: SplitGuids(entity.RoleIdsCsv),
            CreatedUtc: entity.CreatedUtc,
            CreatedByUserId: TryParseGuid(entity.CreatedByUserId),
            ExpiresUtc: entity.ExpiresUtc,
            LastUsedUtc: entity.LastUsedUtc,
            LastUsedIp: entity.LastUsedIp,
            RevokedUtc: entity.RevokedUtc,
            RevokedByUserId: TryParseGuid(entity.RevokedByUserId),
            RevocationReason: entity.RevocationReason,
            IsDeleted: entity.IsDeleted);

    private static string JoinStrings(IEnumerable<string> values)
        => string.Join(",", values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase));

    private static string JoinGuids(IEnumerable<Guid> values)
        => string.Join(",", values.Where(x => x != Guid.Empty).Distinct().Select(x => x.ToString("D")));

    private static IReadOnlyList<string> SplitStrings(string? value)
        => (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<Guid> SplitGuids(string? value)
        => SplitStrings(value)
            .Where(x => Guid.TryParse(x, out _))
            .Select(Guid.Parse)
            .Distinct()
            .ToList();

    private static Guid? TryParseGuid(string? value)
        => Guid.TryParse(value, out var guid) && guid != Guid.Empty ? guid : null;

    private TableClient Table()
        => _serviceClient.GetTableClient(_opts.FullTableName(_opts.ApiCredentialsTableName));
}
