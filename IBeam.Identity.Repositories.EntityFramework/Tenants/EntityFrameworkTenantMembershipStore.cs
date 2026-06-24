using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Repositories.EntityFramework.Data;
using Microsoft.EntityFrameworkCore;

namespace IBeam.Identity.Repositories.EntityFramework.Tenants;

public sealed class EntityFrameworkTenantMembershipStore : ITenantMembershipStore
{
    private readonly IBeamIdentityDbContext _db;

    public EntityFrameworkTenantMembershipStore(IBeamIdentityDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<IReadOnlyList<TenantInfo>> GetTenantsForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var memberships = await _db.TenantUsers
            .AsNoTracking()
            .Include(x => x.Tenant)
            .Where(x => x.UserId == userId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return memberships.Select(x => new TenantInfo(
            x.TenantId,
            x.Tenant.Name,
            SplitRoles(x.RolesCsv),
            IsActiveStatus(x.Status))).ToList();
    }

    public async Task<TenantInfo?> GetTenantForUserAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        var membership = await _db.TenantUsers
            .AsNoTracking()
            .Include(x => x.Tenant)
            .FirstOrDefaultAsync(x => x.UserId == userId && x.TenantId == tenantId, ct)
            .ConfigureAwait(false);

        return membership is null
            ? null
            : new TenantInfo(
                membership.TenantId,
                membership.Tenant.Name,
                SplitRoles(membership.RolesCsv),
                IsActiveStatus(membership.Status));
    }

    public async Task<IReadOnlyList<TenantUserInfo>> GetUsersForTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        var memberships = await _db.TenantUsers
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return memberships.Select(MapTenantUser).ToList();
    }

    public async Task<TenantUserInfo?> GetUserForTenantAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var membership = await _db.TenantUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId, ct)
            .ConfigureAwait(false);

        return membership is null ? null : MapTenantUser(membership);
    }

    public async Task<Guid?> GetDefaultTenantIdAsync(Guid userId, CancellationToken ct = default)
    {
        var membership = await _db.TenantUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.IsDefault, ct)
            .ConfigureAwait(false);

        return membership?.TenantId;
    }

    public async Task SetDefaultTenantAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        var memberships = await _db.TenantUsers
            .Where(x => x.UserId == userId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (!memberships.Any(x => x.TenantId == tenantId))
            throw new InvalidOperationException($"User '{userId}' is not a member of tenant '{tenantId}'.");

        var now = DateTimeOffset.UtcNow;
        foreach (var membership in memberships)
        {
            var isDefault = membership.TenantId == tenantId;
            membership.IsDefault = isDefault;
            if (isDefault)
                membership.LastSelectedAt = now;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DisableTenantMembershipAsync(Guid tenantId, Guid userId, string? reason = null, CancellationToken ct = default)
    {
        var membership = await _db.TenantUsers
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId, ct)
            .ConfigureAwait(false);

        if (membership is null)
            throw new InvalidOperationException($"User '{userId}' is not a member of tenant '{tenantId}'.");

        membership.Status = "Disabled";
        membership.IsDefault = false;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static TenantUserInfo MapTenantUser(Entities.TenantUser membership)
        => new(
            TenantId: membership.TenantId,
            UserId: membership.UserId,
            Roles: SplitRoles(membership.RolesCsv),
            IsActive: IsActiveStatus(membership.Status));

    private static string[] SplitRoles(string? csv)
        => (csv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsActiveStatus(string? status)
        => string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase);
}
