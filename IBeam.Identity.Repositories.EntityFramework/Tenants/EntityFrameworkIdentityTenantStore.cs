using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Repositories.EntityFramework.Data;
using IBeam.Identity.Repositories.EntityFramework.Tenants.Entities;
using Microsoft.EntityFrameworkCore;

namespace IBeam.Identity.Repositories.EntityFramework.Tenants;

public sealed class EntityFrameworkIdentityTenantStore : IIdentityTenantStore
{
    private readonly IBeamIdentityDbContext _db;

    public EntityFrameworkIdentityTenantStore(IBeamIdentityDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<IdentityTenant?> FindByIdAsync(Guid tenantId, CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);

        var tenant = await _db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tenantId, ct)
            .ConfigureAwait(false);

        return tenant is null ? null : Map(tenant);
    }

    public async Task<IdentityTenant> CreateAsync(IdentityTenant tenant, CancellationToken ct = default)
    {
        ValidateTenant(tenant);

        var entity = ToEntity(tenant);
        _db.Tenants.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Map(entity);
    }

    public async Task<IdentityTenant> UpdateAsync(IdentityTenant tenant, CancellationToken ct = default)
    {
        ValidateTenant(tenant);

        var entity = await _db.Tenants
            .FirstOrDefaultAsync(x => x.Id == tenant.TenantId, ct)
            .ConfigureAwait(false)
            ?? throw new IdentityNotFoundException($"Tenant '{tenant.TenantId}' was not found.");

        entity.Name = tenant.Name.Trim();
        entity.NormalizedName = string.IsNullOrWhiteSpace(tenant.NormalizedName)
            ? IdentityTenant.NormalizeName(tenant.Name)
            : tenant.NormalizedName.Trim();
        entity.Status = string.IsNullOrWhiteSpace(tenant.Status)
            ? IdentityTenantStatuses.Active
            : tenant.Status.Trim();
        entity.UpdatedAt = tenant.UpdatedAt ?? DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Map(entity);
    }

    public async Task<IdentityTenant> SetStatusAsync(Guid tenantId, string status, CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        if (string.IsNullOrWhiteSpace(status))
            throw new IdentityValidationException("Tenant status is required.");

        var entity = await _db.Tenants
            .FirstOrDefaultAsync(x => x.Id == tenantId, ct)
            .ConfigureAwait(false)
            ?? throw new IdentityNotFoundException($"Tenant '{tenantId}' was not found.");

        entity.Status = status.Trim();
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Map(entity);
    }

    private static Tenant ToEntity(IdentityTenant tenant)
        => new()
        {
            Id = tenant.TenantId,
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

    private static IdentityTenant Map(Tenant tenant)
        => new(
            tenant.Id,
            tenant.Name,
            tenant.NormalizedName,
            string.IsNullOrWhiteSpace(tenant.Status) ? IdentityTenantStatuses.Active : tenant.Status,
            tenant.CreatedAt,
            tenant.UpdatedAt);

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
