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

public sealed class AzureTableAccessGrantStore : IIBeamAccessGrantStore
{
    private readonly TableServiceClient _serviceClient;
    private readonly AzureTableIdentityOptions _opts;

    public AzureTableAccessGrantStore(TableServiceClient serviceClient, IOptions<AzureTableIdentityOptions> opts)
    {
        _serviceClient = serviceClient;
        _opts = opts.Value;
    }

    public async Task<IReadOnlyList<AccessGrant>> GetGrantsAsync(
        Guid tenantId,
        string? subjectType = null,
        string? subjectId = null,
        CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            ValidateTenantId(tenantId);

            var normalizedSubjectType = NormalizeOptional(subjectType);
            var normalizedSubjectId = NormalizeOptional(subjectId);
            var list = new List<AccessGrant>();
            var pk = _opts.AccessGrantsPk(tenantId);

            await foreach (var entity in Table().QueryAsync<AccessGrantEntity>(x => x.PartitionKey == pk, cancellationToken: ct))
            {
                if (!IsActive(entity.Status))
                    continue;

                if (normalizedSubjectType is not null &&
                    !string.Equals(entity.SubjectType, normalizedSubjectType, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (normalizedSubjectId is not null &&
                    !string.Equals(entity.SubjectId, normalizedSubjectId, StringComparison.OrdinalIgnoreCase))
                    continue;

                list.Add(Map(entity));
            }

            return list
                .OrderBy(x => x.SubjectType, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.SubjectId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.ResourceType, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.ResourceId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<AccessGrant?> GetGrantAsync(Guid tenantId, Guid grantId, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            ValidateTenantId(tenantId);
            ValidateGrantId(grantId);

            var response = await Table()
                .GetEntityIfExistsAsync<AccessGrantEntity>(
                    _opts.AccessGrantsPk(tenantId),
                    _opts.AccessGrantsRk(grantId),
                    cancellationToken: ct)
                .ConfigureAwait(false);

            if (!response.HasValue || !IsActive(response.Value.Status))
                return null;

            return Map(response.Value);
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<AccessGrant> UpsertGrantAsync(
        Guid tenantId,
        Guid? grantId,
        string subjectType,
        string subjectId,
        string resourceType,
        string resourceId,
        string accessLevel,
        CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            ValidateTenantId(tenantId);

            var id = grantId.GetValueOrDefault(Guid.NewGuid());
            ValidateGrantId(id);

            var now = DateTimeOffset.UtcNow;
            var existing = await Table()
                .GetEntityIfExistsAsync<AccessGrantEntity>(
                    _opts.AccessGrantsPk(tenantId),
                    _opts.AccessGrantsRk(id),
                    cancellationToken: ct)
                .ConfigureAwait(false);

            var entity = existing.HasValue
                ? existing.Value
                : new AccessGrantEntity
                {
                    PartitionKey = _opts.AccessGrantsPk(tenantId),
                    RowKey = _opts.AccessGrantsRk(id),
                    TenantId = tenantId.ToString("D"),
                    GrantId = id.ToString("D"),
                    CreatedAt = now
                };

            entity.SubjectType = NormalizeRequired(subjectType, "subjectType");
            entity.SubjectId = NormalizeRequired(subjectId, "subjectId");
            entity.ResourceType = NormalizeRequired(resourceType, "resourceType");
            entity.ResourceId = NormalizeRequired(resourceId, "resourceId");
            entity.AccessLevel = NormalizeRequired(accessLevel, "accessLevel");
            entity.Status = "Active";
            entity.UpdatedAt = now;

            await Table().UpsertEntityAsync(entity, TableUpdateMode.Replace, ct).ConfigureAwait(false);
            return Map(entity);
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task DeleteGrantAsync(Guid tenantId, Guid grantId, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            ValidateTenantId(tenantId);
            ValidateGrantId(grantId);

            var response = await Table()
                .GetEntityIfExistsAsync<AccessGrantEntity>(
                    _opts.AccessGrantsPk(tenantId),
                    _opts.AccessGrantsRk(grantId),
                    cancellationToken: ct)
                .ConfigureAwait(false);

            if (!response.HasValue)
                return;

            var entity = response.Value;
            entity.Status = "Disabled";
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            await Table().UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, ct).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    private TableClient Table()
        => _serviceClient.GetTableClient(_opts.FullTableName(_opts.AccessGrantsTableName));

    private static void ValidateTenantId(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new IdentityValidationException("tenantId is required.");
    }

    private static void ValidateGrantId(Guid grantId)
    {
        if (grantId == Guid.Empty)
            throw new IdentityValidationException("grantId is required.");
    }

    private static string NormalizeRequired(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new IdentityValidationException($"{name} is required.");

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsActive(string? status)
        => string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase);

    private static AccessGrant Map(AccessGrantEntity entity)
        => new(
            GrantId: Guid.TryParse(entity.GrantId, out var grantId) ? grantId : Guid.Empty,
            TenantId: Guid.TryParse(entity.TenantId, out var tenantId) ? tenantId : Guid.Empty,
            SubjectType: entity.SubjectType,
            SubjectId: entity.SubjectId,
            ResourceType: entity.ResourceType,
            ResourceId: entity.ResourceId,
            AccessLevel: entity.AccessLevel,
            IsActive: IsActive(entity.Status),
            CreatedAt: entity.CreatedAt,
            UpdatedAt: entity.UpdatedAt);
}

