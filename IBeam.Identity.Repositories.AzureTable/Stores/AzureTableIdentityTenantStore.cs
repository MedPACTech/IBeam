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

public sealed class AzureTableIdentityTenantStore : IIdentityTenantStore
{
    private readonly TableServiceClient _serviceClient;
    private readonly AzureTableIdentityOptions _opts;

    public AzureTableIdentityTenantStore(TableServiceClient serviceClient, IOptions<AzureTableIdentityOptions> opts)
    {
        _serviceClient = serviceClient ?? throw new ArgumentNullException(nameof(serviceClient));
        _opts = opts?.Value ?? throw new ArgumentNullException(nameof(opts));
    }

    public async Task<IdentityTenant?> FindByIdAsync(Guid tenantId, CancellationToken ct = default)
    {
        try
        {
            ValidateTenantId(tenantId);

            var resp = await TenantsTable()
                .GetEntityIfExistsAsync<TenantEntity>(
                    TenantEntity.TenantsPartitionKey,
                    tenantId.ToString("D"),
                    cancellationToken: ct)
                .ConfigureAwait(false);

            return resp.HasValue ? Map(resp.Value) : null;
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<IdentityTenant> CreateAsync(IdentityTenant tenant, CancellationToken ct = default)
    {
        try
        {
            ValidateTenant(tenant);

            var entity = ToEntity(tenant);
            await TenantsTable().AddEntityAsync(entity, ct).ConfigureAwait(false);
            return Map(entity);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            throw new IdentityValidationException($"Tenant '{tenant.TenantId}' already exists.");
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<IdentityTenant> UpdateAsync(IdentityTenant tenant, CancellationToken ct = default)
    {
        try
        {
            ValidateTenant(tenant);

            var resp = await TenantsTable()
                .GetEntityIfExistsAsync<TenantEntity>(
                    TenantEntity.TenantsPartitionKey,
                    tenant.TenantId.ToString("D"),
                    cancellationToken: ct)
                .ConfigureAwait(false);

            if (!resp.HasValue)
                throw new IdentityNotFoundException($"Tenant '{tenant.TenantId}' was not found.");

            var existing = resp.Value;
            existing.Name = tenant.Name.Trim();
            existing.NormalizedName = string.IsNullOrWhiteSpace(tenant.NormalizedName)
                ? IdentityTenant.NormalizeName(tenant.Name)
                : tenant.NormalizedName.Trim();
            existing.Status = string.IsNullOrWhiteSpace(tenant.Status)
                ? IdentityTenantStatuses.Active
                : tenant.Status.Trim();
            existing.UpdatedAt = tenant.UpdatedAt ?? DateTimeOffset.UtcNow;

            await TenantsTable()
                .UpdateEntityAsync(existing, existing.ETag, TableUpdateMode.Replace, ct)
                .ConfigureAwait(false);

            return Map(existing);
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    public async Task<IdentityTenant> SetStatusAsync(Guid tenantId, string status, CancellationToken ct = default)
    {
        try
        {
            ValidateTenantId(tenantId);
            if (string.IsNullOrWhiteSpace(status))
                throw new IdentityValidationException("Tenant status is required.");

            var resp = await TenantsTable()
                .GetEntityIfExistsAsync<TenantEntity>(
                    TenantEntity.TenantsPartitionKey,
                    tenantId.ToString("D"),
                    cancellationToken: ct)
                .ConfigureAwait(false);

            if (!resp.HasValue)
                throw new IdentityNotFoundException($"Tenant '{tenantId}' was not found.");

            var entity = resp.Value;
            entity.Status = status.Trim();
            entity.UpdatedAt = DateTimeOffset.UtcNow;

            await TenantsTable()
                .UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, ct)
                .ConfigureAwait(false);

            return Map(entity);
        }
        catch (Exception ex)
        {
            throw IdentityExceptionTranslator.ToProviderException(ex);
        }
    }

    private TableClient TenantsTable()
        => _serviceClient.GetTableClient(_opts.FullTableName(_opts.TenantsTableName));

    private static TenantEntity ToEntity(IdentityTenant tenant)
        => new()
        {
            PartitionKey = TenantEntity.TenantsPartitionKey,
            RowKey = tenant.TenantId.ToString("D"),
            Name = tenant.Name.Trim(),
            NormalizedName = string.IsNullOrWhiteSpace(tenant.NormalizedName)
                ? IdentityTenant.NormalizeName(tenant.Name)
                : tenant.NormalizedName.Trim(),
            Status = string.IsNullOrWhiteSpace(tenant.Status)
                ? IdentityTenantStatuses.Active
                : tenant.Status.Trim(),
            CreatedAt = tenant.CreatedAt ?? DateTimeOffset.UtcNow,
            UpdatedAt = tenant.UpdatedAt
        };

    private static IdentityTenant Map(TenantEntity entity)
        => new(
            Guid.Parse(entity.RowKey),
            entity.Name,
            entity.NormalizedName,
            string.IsNullOrWhiteSpace(entity.Status) ? IdentityTenantStatuses.Active : entity.Status,
            entity.CreatedAt,
            entity.UpdatedAt);

    private static void ValidateTenant(IdentityTenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ValidateTenantId(tenant.TenantId);

        if (string.IsNullOrWhiteSpace(tenant.Name))
            throw new IdentityValidationException("Tenant name is required.");
    }

    private static void ValidateTenantId(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new IdentityValidationException("tenantId is required.");
    }
}
