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

public sealed class AzureTableAgentUserStore : IAgentUserStore
{
    private readonly TableServiceClient _serviceClient;
    private readonly AzureTableIdentityOptions _opts;

    public AzureTableAgentUserStore(TableServiceClient serviceClient, IOptions<AzureTableIdentityOptions> opts)
    {
        _serviceClient = serviceClient;
        _opts = opts.Value;
    }

    public async Task<AgentUserRecord> CreateAsync(AgentUserRecord agentUser, CancellationToken ct = default)
    {
        try
        {
            var entity = Map(agentUser);
            await AgentUsersTable().AddEntityAsync(entity, ct).ConfigureAwait(false);
            return Map(entity);
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<IReadOnlyList<AgentUserRecord>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        try
        {
            var pk = _opts.AgentUsersPk(tenantId);
            var list = new List<AgentUserRecord>();
            await foreach (var entity in AgentUsersTable().QueryAsync<AgentUserEntity>(
                x => x.PartitionKey == pk,
                cancellationToken: ct))
            {
                list.Add(Map(entity));
            }

            return list
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<AgentUserRecord?> GetAsync(Guid tenantId, Guid agentUserId, CancellationToken ct = default)
    {
        try
        {
            var response = await AgentUsersTable()
                .GetEntityIfExistsAsync<AgentUserEntity>(
                    _opts.AgentUsersPk(tenantId),
                    _opts.AgentUsersRk(agentUserId),
                    cancellationToken: ct)
                .ConfigureAwait(false);

            return response.HasValue ? Map(response.Value) : null;
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<AgentUserRecord> UpdateAsync(AgentUserRecord agentUser, CancellationToken ct = default)
    {
        try
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    var entity = await RequireAgentUserEntityAsync(agentUser.TenantId, agentUser.AgentUserId, ct)
                        .ConfigureAwait(false);
                    entity.DisplayName = agentUser.DisplayName;
                    entity.Description = agentUser.Description;
                    entity.AgentType = agentUser.AgentType;
                    entity.AgentKey = agentUser.AgentKey;
                    entity.Status = agentUser.Status;
                    entity.UpdatedUtc = agentUser.UpdatedUtc;
                    entity.MetadataJson = agentUser.MetadataJson;
                    await AgentUsersTable().UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, ct)
                        .ConfigureAwait(false);
                    return Map(entity);
                }
                catch (RequestFailedException ex) when (ex.Status == 412 && attempt < 4)
                {
                }
            }

            throw new IdentityProviderException("Failed to update agent user due to concurrent updates.");
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<AgentUserCredentialBindingRecord> BindCredentialAsync(
        AgentUserCredentialBindingRecord binding,
        CancellationToken ct = default)
    {
        try
        {
            var existing = await GetCredentialBindingAsync(binding.TenantId, binding.CredentialId, ct)
                .ConfigureAwait(false);
            if (existing is not null && existing.IsActive)
                throw new IdentityValidationException("API credential is already assigned to an active agent user.");

            var entity = Map(binding);
            await AgentUserCredentialsTable().UpsertEntityAsync(entity, TableUpdateMode.Replace, ct)
                .ConfigureAwait(false);
            await AgentUserCredentialsTable().UpsertEntityAsync(Index(binding), TableUpdateMode.Replace, ct)
                .ConfigureAwait(false);
            return Map(entity);
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<IReadOnlyList<AgentUserCredentialBindingRecord>> ListCredentialBindingsAsync(
        Guid tenantId,
        Guid agentUserId,
        CancellationToken ct = default)
    {
        try
        {
            var pk = _opts.AgentUserCredentialsPk(tenantId, agentUserId);
            var list = new List<AgentUserCredentialBindingRecord>();
            await foreach (var entity in AgentUserCredentialsTable().QueryAsync<AgentUserCredentialEntity>(
                x => x.PartitionKey == pk,
                cancellationToken: ct))
            {
                list.Add(Map(entity));
            }

            return list
                .OrderByDescending(x => x.CreatedUtc)
                .ToList();
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<AgentUserCredentialBindingRecord?> GetCredentialBindingAsync(
        Guid tenantId,
        Guid credentialId,
        CancellationToken ct = default)
    {
        try
        {
            var indexResponse = await AgentUserCredentialsTable()
                .GetEntityIfExistsAsync<TableEntity>(
                    _opts.AgentCredentialIndexPk(tenantId),
                    _opts.AgentCredentialIndexRk(credentialId),
                    cancellationToken: ct)
                .ConfigureAwait(false);

            if (!indexResponse.HasValue ||
                !indexResponse.Value.TryGetValue("AgentUserId", out var agentUserIdValue) ||
                !Guid.TryParse(agentUserIdValue?.ToString(), out var agentUserId))
            {
                return null;
            }

            var response = await AgentUserCredentialsTable()
                .GetEntityIfExistsAsync<AgentUserCredentialEntity>(
                    _opts.AgentUserCredentialsPk(tenantId, agentUserId),
                    _opts.AgentUserCredentialsRk(credentialId),
                    cancellationToken: ct)
                .ConfigureAwait(false);

            return response.HasValue ? Map(response.Value) : null;
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task RevokeCredentialBindingAsync(
        Guid tenantId,
        Guid agentUserId,
        Guid credentialId,
        Guid? revokedByUserId,
        CancellationToken ct = default)
    {
        try
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    var entity = await RequireBindingEntityAsync(tenantId, agentUserId, credentialId, ct)
                        .ConfigureAwait(false);
                    entity.Status = AgentUserStatuses.Disabled;
                    entity.RevokedUtc ??= DateTimeOffset.UtcNow;
                    entity.RevokedByUserId ??= revokedByUserId?.ToString("D");
                    await AgentUserCredentialsTable().UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, ct)
                        .ConfigureAwait(false);
                    await AgentUserCredentialsTable().DeleteEntityAsync(
                        _opts.AgentCredentialIndexPk(tenantId),
                        _opts.AgentCredentialIndexRk(credentialId),
                        ETag.All,
                        ct).ConfigureAwait(false);
                    return;
                }
                catch (RequestFailedException ex) when (ex.Status == 412 && attempt < 4)
                {
                }
                catch (RequestFailedException ex) when (ex.Status == 404)
                {
                    return;
                }
            }

            throw new IdentityProviderException("Failed to revoke agent user credential binding due to concurrent updates.");
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    private async Task<AgentUserEntity> RequireAgentUserEntityAsync(Guid tenantId, Guid agentUserId, CancellationToken ct)
    {
        var response = await AgentUsersTable()
            .GetEntityIfExistsAsync<AgentUserEntity>(
                _opts.AgentUsersPk(tenantId),
                _opts.AgentUsersRk(agentUserId),
                cancellationToken: ct)
            .ConfigureAwait(false);

        if (!response.HasValue)
            throw new IdentityNotFoundException($"Agent user '{agentUserId}' was not found.");

        return response.Value;
    }

    private async Task<AgentUserCredentialEntity> RequireBindingEntityAsync(
        Guid tenantId,
        Guid agentUserId,
        Guid credentialId,
        CancellationToken ct)
    {
        var response = await AgentUserCredentialsTable()
            .GetEntityIfExistsAsync<AgentUserCredentialEntity>(
                _opts.AgentUserCredentialsPk(tenantId, agentUserId),
                _opts.AgentUserCredentialsRk(credentialId),
                cancellationToken: ct)
            .ConfigureAwait(false);

        if (!response.HasValue)
            throw new IdentityNotFoundException($"Agent user credential binding for '{credentialId}' was not found.");

        return response.Value;
    }

    private AgentUserEntity Map(AgentUserRecord record)
        => new()
        {
            PartitionKey = _opts.AgentUsersPk(record.TenantId),
            RowKey = _opts.AgentUsersRk(record.AgentUserId),
            AgentUserId = record.AgentUserId.ToString("D"),
            TenantId = record.TenantId.ToString("D"),
            DisplayName = record.DisplayName,
            Description = record.Description,
            AgentType = record.AgentType,
            AgentKey = record.AgentKey,
            Status = record.Status,
            CreatedUtc = record.CreatedUtc,
            CreatedByUserId = record.CreatedByUserId?.ToString("D"),
            UpdatedUtc = record.UpdatedUtc,
            MetadataJson = record.MetadataJson
        };

    private static AgentUserRecord Map(AgentUserEntity entity)
        => new(
            Guid.Parse(entity.AgentUserId),
            Guid.Parse(entity.TenantId),
            entity.DisplayName,
            entity.Description,
            entity.AgentType,
            entity.AgentKey,
            entity.Status,
            entity.CreatedUtc,
            TryParseGuid(entity.CreatedByUserId),
            entity.UpdatedUtc,
            entity.MetadataJson);

    private AgentUserCredentialEntity Map(AgentUserCredentialBindingRecord record)
        => new()
        {
            PartitionKey = _opts.AgentUserCredentialsPk(record.TenantId, record.AgentUserId),
            RowKey = _opts.AgentUserCredentialsRk(record.CredentialId),
            BindingId = record.BindingId.ToString("D"),
            TenantId = record.TenantId.ToString("D"),
            AgentUserId = record.AgentUserId.ToString("D"),
            CredentialId = record.CredentialId.ToString("D"),
            Purpose = record.Purpose,
            EnvironmentKey = record.EnvironmentKey,
            Status = record.Status,
            CreatedUtc = record.CreatedUtc,
            CreatedByUserId = record.CreatedByUserId?.ToString("D"),
            RevokedUtc = record.RevokedUtc,
            RevokedByUserId = record.RevokedByUserId?.ToString("D"),
            MetadataJson = record.MetadataJson
        };

    private static AgentUserCredentialBindingRecord Map(AgentUserCredentialEntity entity)
        => new(
            Guid.Parse(entity.BindingId),
            Guid.Parse(entity.TenantId),
            Guid.Parse(entity.AgentUserId),
            Guid.Parse(entity.CredentialId),
            entity.Purpose,
            entity.EnvironmentKey,
            entity.Status,
            entity.CreatedUtc,
            TryParseGuid(entity.CreatedByUserId),
            entity.RevokedUtc,
            TryParseGuid(entity.RevokedByUserId),
            entity.MetadataJson);

    private static Guid? TryParseGuid(string? value)
        => Guid.TryParse(value, out var guid) && guid != Guid.Empty ? guid : null;

    private TableEntity Index(AgentUserCredentialBindingRecord record)
        => new(_opts.AgentCredentialIndexPk(record.TenantId), _opts.AgentCredentialIndexRk(record.CredentialId))
        {
            ["TenantId"] = record.TenantId.ToString("D"),
            ["AgentUserId"] = record.AgentUserId.ToString("D"),
            ["CredentialId"] = record.CredentialId.ToString("D"),
            ["BindingId"] = record.BindingId.ToString("D"),
            ["Status"] = record.Status,
            ["UpdatedUtc"] = DateTimeOffset.UtcNow
        };

    private TableClient AgentUsersTable()
        => _serviceClient.GetTableClient(_opts.FullTableName(_opts.AgentUsersTableName));

    private TableClient AgentUserCredentialsTable()
        => _serviceClient.GetTableClient(_opts.FullTableName(_opts.AgentUserCredentialsTableName));
}
