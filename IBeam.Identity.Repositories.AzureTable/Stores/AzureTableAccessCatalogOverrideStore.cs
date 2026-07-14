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

public sealed class AzureTableAccessCatalogOverrideStore : IIBeamAccessCatalogOverrideStore
{
    private readonly TableServiceClient _serviceClient;
    private readonly AzureTableIdentityOptions _opts;

    public AzureTableAccessCatalogOverrideStore(TableServiceClient serviceClient, IOptions<AzureTableIdentityOptions> opts)
    {
        _serviceClient = serviceClient;
        _opts = opts.Value;
    }

    public async Task<IReadOnlyList<AccessCatalogOverride>> GetOverridesAsync(Guid tenantId, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            ValidateTenantId(tenantId);

            var list = new List<AccessCatalogOverride>();
            var pk = _opts.AccessCatalogOverridesPk(tenantId);

            await foreach (var entity in Table().QueryAsync<AccessCatalogOverrideEntity>(x => x.PartitionKey == pk, cancellationToken: ct))
            {
                if (IsActive(entity.Status))
                    list.Add(Map(entity));
            }

            return list
                .OrderBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<AccessCatalogOverride?> GetOverrideAsync(Guid tenantId, Guid catalogItemId, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            ValidateTenantId(tenantId);
            ValidateCatalogItemId(catalogItemId);

            var response = await Table()
                .GetEntityIfExistsAsync<AccessCatalogOverrideEntity>(
                    _opts.AccessCatalogOverridesPk(tenantId),
                    _opts.AccessCatalogOverridesRk(catalogItemId),
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

    public async Task<AccessCatalogOverride> UpsertOverrideAsync(
        Guid tenantId,
        Guid? catalogItemId,
        UpsertAccessCatalogOverrideRequest request,
        CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            ValidateTenantId(tenantId);
            if (request is null)
                throw new ArgumentNullException(nameof(request));

            var id = catalogItemId.GetValueOrDefault(Guid.NewGuid());
            ValidateCatalogItemId(id);

            var now = DateTimeOffset.UtcNow;
            var existing = await Table()
                .GetEntityIfExistsAsync<AccessCatalogOverrideEntity>(
                    _opts.AccessCatalogOverridesPk(tenantId),
                    _opts.AccessCatalogOverridesRk(id),
                    cancellationToken: ct)
                .ConfigureAwait(false);

            var entity = existing.HasValue
                ? existing.Value
                : new AccessCatalogOverrideEntity
                {
                    PartitionKey = _opts.AccessCatalogOverridesPk(tenantId),
                    RowKey = _opts.AccessCatalogOverridesRk(id),
                    TenantId = tenantId.ToString("D"),
                    CatalogItemId = id.ToString("D"),
                    CreatedAt = now
                };

            entity.Key = NormalizeRequired(request.Key, "key");
            entity.Label = NormalizeRequired(request.Label, "label");
            entity.Description = NormalizeOptional(request.Description);
            entity.Category = NormalizeRequired(request.Category, "category");
            entity.IsAssignable = request.IsAssignable;
            entity.IsMutable = request.IsMutable;
            entity.IsEnabled = request.IsEnabled;
            entity.SubjectTypesCsv = SerializeCsv(request.SubjectTypes);
            entity.ResourceType = NormalizeOptional(request.ResourceType);
            entity.ResourceId = NormalizeOptional(request.ResourceId);
            entity.ParentResourceType = NormalizeOptional(request.ParentResourceType);
            entity.ParentResourceId = NormalizeOptional(request.ParentResourceId);
            entity.SupportedAccessLevelsCsv = SerializeCsv(request.SupportedAccessLevels);
            entity.Rank = request.Rank;
            entity.ModuleKey = NormalizeOptional(request.ModuleKey);
            entity.RequiredAccessLevel = NormalizeOptional(request.RequiredAccessLevel);
            entity.IsDangerous = request.IsDangerous;
            entity.IdParameter = NormalizeOptional(request.IdParameter);
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

    public async Task DeleteOverrideAsync(Guid tenantId, Guid catalogItemId, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            ValidateTenantId(tenantId);
            ValidateCatalogItemId(catalogItemId);

            var response = await Table()
                .GetEntityIfExistsAsync<AccessCatalogOverrideEntity>(
                    _opts.AccessCatalogOverridesPk(tenantId),
                    _opts.AccessCatalogOverridesRk(catalogItemId),
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
        => _serviceClient.GetTableClient(_opts.FullTableName(_opts.AccessCatalogOverridesTableName));

    private static AccessCatalogOverride Map(AccessCatalogOverrideEntity entity)
        => new(
            Guid.TryParse(entity.CatalogItemId, out var catalogItemId) ? catalogItemId : Guid.Empty,
            Guid.TryParse(entity.TenantId, out var tenantId) ? tenantId : Guid.Empty,
            entity.Key,
            entity.Label,
            entity.Description,
            entity.Category,
            entity.IsAssignable,
            entity.IsMutable,
            entity.IsEnabled,
            ParseCsv(entity.SubjectTypesCsv),
            entity.ResourceType,
            entity.ResourceId,
            entity.ParentResourceType,
            entity.ParentResourceId,
            ParseCsv(entity.SupportedAccessLevelsCsv),
            entity.Rank,
            entity.ModuleKey,
            entity.RequiredAccessLevel,
            entity.IsDangerous,
            entity.IdParameter,
            entity.CreatedAt,
            entity.UpdatedAt);

    private static void ValidateTenantId(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new IdentityValidationException("tenantId is required.");
    }

    private static void ValidateCatalogItemId(Guid catalogItemId)
    {
        if (catalogItemId == Guid.Empty)
            throw new IdentityValidationException("catalogItemId is required.");
    }

    private static string NormalizeRequired(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new IdentityValidationException($"{name} is required.");

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SerializeCsv(IEnumerable<string>? values)
    {
        var normalized = values?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return normalized is { Count: > 0 } ? string.Join(",", normalized) : null;
    }

    private static IReadOnlyList<string> ParseCsv(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    private static bool IsActive(string? status)
        => string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase);
}
