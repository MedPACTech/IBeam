using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;

namespace IBeam.Identity.Services.Tenants;

public sealed class TenantRoleService : ITenantRoleService
{
    private readonly ITenantRoleStore _roles;

    public TenantRoleService(ITenantRoleStore roles)
    {
        _roles = roles ?? throw new ArgumentNullException(nameof(roles));
    }

    public Task<IReadOnlyList<TenantRole>> GetRolesAsync(Guid tenantId, CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        return _roles.GetRolesAsync(tenantId, ct);
    }

    public Task<TenantRole?> GetRoleAsync(Guid tenantId, Guid roleId, CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        ValidateRoleId(roleId);
        return _roles.GetRoleAsync(tenantId, roleId, ct);
    }

    public Task<TenantRole> CreateRoleAsync(Guid tenantId, string name, CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        var normalizedName = NormalizeRoleName(name);
        return _roles.CreateRoleAsync(tenantId, normalizedName, isSystem: false, ct);
    }

    public Task<TenantRole> UpdateRoleAsync(Guid tenantId, Guid roleId, string name, CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        ValidateRoleId(roleId);
        var normalizedName = NormalizeRoleName(name);
        return _roles.UpdateRoleAsync(tenantId, roleId, normalizedName, ct);
    }

    public Task DeleteRoleAsync(Guid tenantId, Guid roleId, CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        ValidateRoleId(roleId);
        return _roles.DeleteRoleAsync(tenantId, roleId, ct);
    }

    public Task<UserTenantRoleAssignment> GrantRolesAsync(Guid tenantId, Guid userId, IReadOnlyList<Guid> roleIds, CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        ValidateUserId(userId);
        ValidateRoleIds(roleIds);
        return _roles.GrantRolesAsync(tenantId, userId, roleIds, ct);
    }

    public Task<UserTenantRoleAssignment> RevokeRolesAsync(Guid tenantId, Guid userId, IReadOnlyList<Guid> roleIds, CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        ValidateUserId(userId);
        ValidateRoleIds(roleIds);
        return _roles.RevokeRolesAsync(tenantId, userId, roleIds, ct);
    }

    public Task<IReadOnlyList<TenantRole>> GetRolesForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        ValidateUserId(userId);
        return _roles.GetRolesForUserAsync(tenantId, userId, ct);
    }

    private static void ValidateTenantId(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new IdentityValidationException("tenantId is required.");
    }

    private static void ValidateRoleId(Guid roleId)
    {
        if (roleId == Guid.Empty)
            throw new IdentityValidationException("roleId is required.");
    }

    private static void ValidateUserId(Guid userId)
    {
        if (userId == Guid.Empty)
            throw new IdentityValidationException("userId is required.");
    }

    private static void ValidateRoleIds(IReadOnlyList<Guid> roleIds)
    {
        if (roleIds is null || roleIds.Count == 0)
            throw new IdentityValidationException("At least one roleId is required.");
        if (roleIds.Any(x => x == Guid.Empty))
            throw new IdentityValidationException("roleIds cannot contain empty GUID values.");
    }

    private static string NormalizeRoleName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new IdentityValidationException("Role name is required.");

        var value = name.Trim();
        if (value.Length < 2 || value.Length > 64)
            throw new IdentityValidationException("Role name must be between 2 and 64 characters.");

        return value;
    }
}
