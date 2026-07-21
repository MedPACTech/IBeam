using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Services.Abstractions;

namespace IBeam.Identity.Services.Tenants;

[IBeamOperation("identity.tenantroles")]
public sealed class TenantRoleService : ITenantRoleService
{
    private readonly ITenantRoleStore _roles;
    private readonly ITenantExtensionCoordinator _tenantExtensions;
    private readonly IIdentityUserStore? _users;
    private readonly IServiceOperationExecutor _operations;

    public TenantRoleService(ITenantRoleStore roles)
        : this(roles, new NoOpTenantExtensionCoordinator())
    {
    }

    public TenantRoleService(
        ITenantRoleStore roles,
        ITenantExtensionCoordinator tenantExtensions,
        IIdentityUserStore? users = null,
        IServiceOperationExecutor? operations = null)
    {
        _roles = roles ?? throw new ArgumentNullException(nameof(roles));
        _tenantExtensions = tenantExtensions ?? throw new ArgumentNullException(nameof(tenantExtensions));
        _users = users;
        _operations = operations ?? new ServiceOperationExecutor();
    }

    [IBeamOperation("identity.tenantroles.list")]
    public Task<IReadOnlyList<TenantRole>> GetRolesAsync(Guid tenantId, CancellationToken ct = default)
        => _operations.ExecuteAsync(
            this,
            token => GetRolesCoreAsync(tenantId, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId },
            ct);

    private Task<IReadOnlyList<TenantRole>> GetRolesCoreAsync(Guid tenantId, CancellationToken ct)
    {
        ValidateTenantId(tenantId);
        return _roles.GetRolesAsync(tenantId, ct);
    }

    [IBeamOperation("identity.tenantroles.get")]
    public Task<TenantRole?> GetRoleAsync(Guid tenantId, Guid roleId, CancellationToken ct = default)
        => _operations.ExecuteAsync(
            this,
            token => GetRoleCoreAsync(tenantId, roleId, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = roleId },
            ct);

    private Task<TenantRole?> GetRoleCoreAsync(Guid tenantId, Guid roleId, CancellationToken ct)
    {
        ValidateTenantId(tenantId);
        ValidateRoleId(roleId);
        return _roles.GetRoleAsync(tenantId, roleId, ct);
    }

    [IBeamOperation("identity.tenantroles.create")]
    public Task<TenantRole> CreateRoleAsync(Guid tenantId, string name, CancellationToken ct = default)
        => _operations.ExecuteAsync(
            this,
            token => CreateRoleCoreAsync(tenantId, name, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId },
            ct);

    private Task<TenantRole> CreateRoleCoreAsync(Guid tenantId, string name, CancellationToken ct)
    {
        ValidateTenantId(tenantId);
        var normalizedName = NormalizeRoleName(name);
        return _roles.CreateRoleAsync(tenantId, normalizedName, isSystem: false, ct);
    }

    [IBeamOperation("identity.tenantroles.update")]
    public Task<TenantRole> UpdateRoleAsync(Guid tenantId, Guid roleId, string name, CancellationToken ct = default)
        => _operations.ExecuteAsync(
            this,
            token => UpdateRoleCoreAsync(tenantId, roleId, name, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = roleId },
            ct);

    private Task<TenantRole> UpdateRoleCoreAsync(Guid tenantId, Guid roleId, string name, CancellationToken ct)
    {
        ValidateTenantId(tenantId);
        ValidateRoleId(roleId);
        var normalizedName = NormalizeRoleName(name);
        return _roles.UpdateRoleAsync(tenantId, roleId, normalizedName, ct);
    }

    [IBeamOperation("identity.tenantroles.delete")]
    public Task DeleteRoleAsync(Guid tenantId, Guid roleId, CancellationToken ct = default)
        => _operations.ExecuteAsync(
            this,
            token => DeleteRoleCoreAsync(tenantId, roleId, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = roleId },
            ct);

    private Task DeleteRoleCoreAsync(Guid tenantId, Guid roleId, CancellationToken ct)
    {
        ValidateTenantId(tenantId);
        ValidateRoleId(roleId);
        return _roles.DeleteRoleAsync(tenantId, roleId, ct);
    }

    [IBeamOperation("identity.tenantroles.grant")]
    public Task<UserTenantRoleAssignment> GrantRolesAsync(Guid tenantId, Guid userId, IReadOnlyList<Guid> roleIds, CancellationToken ct = default)
        => _operations.ExecuteAsync(
            this,
            token => GrantRolesCoreAsync(tenantId, userId, roleIds, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = userId },
            ct);

    private Task<UserTenantRoleAssignment> GrantRolesCoreAsync(Guid tenantId, Guid userId, IReadOnlyList<Guid> roleIds, CancellationToken ct)
    {
        ValidateTenantId(tenantId);
        ValidateUserId(userId);
        ValidateRoleIds(roleIds);
        return _roles.GrantRolesAsync(tenantId, userId, roleIds, ct);
    }

    [IBeamOperation("identity.tenantroles.bootstrap")]
    public async Task<UserTenantRoleAssignment> EnsureTenantMembershipAndGrantRolesAsync(TenantMembershipRoleBootstrapRequest request, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => EnsureTenantMembershipAndGrantRolesCoreWrapperAsync(request, token),
            new ServiceOperationExecutionOptions { TenantId = request?.TenantId, EntityId = request?.UserId },
            ct).ConfigureAwait(false);

    private async Task<UserTenantRoleAssignment> EnsureTenantMembershipAndGrantRolesCoreWrapperAsync(
        TenantMembershipRoleBootstrapRequest request,
        CancellationToken ct)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        ValidateTenantId(request.TenantId);
        ValidateUserId(request.UserId);
        ValidateBootstrapRoles(request.RoleIds, request.RoleNames);

        var normalizedRequest = request with
        {
            TenantName = string.IsNullOrWhiteSpace(request.TenantName) ? null : request.TenantName.Trim(),
            UserDisplayName = string.IsNullOrWhiteSpace(request.UserDisplayName) ? null : request.UserDisplayName.Trim(),
            UserEmail = string.IsNullOrWhiteSpace(request.UserEmail) ? null : request.UserEmail.Trim().ToLowerInvariant(),
            UserPhoneNumber = string.IsNullOrWhiteSpace(request.UserPhoneNumber) ? null : request.UserPhoneNumber.Trim(),
            RoleIds = request.RoleIds?.Where(x => x != Guid.Empty).Distinct().ToList(),
            RoleNames = request.RoleNames?
                .Select(NormalizeRoleName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        return await EnsureTenantMembershipAndGrantRolesCoreAsync(
            await EnrichUserProjectionAsync(normalizedRequest, ct).ConfigureAwait(false),
            ct).ConfigureAwait(false);
    }

    private async Task<TenantMembershipRoleBootstrapRequest> EnrichUserProjectionAsync(
        TenantMembershipRoleBootstrapRequest request,
        CancellationToken ct)
    {
        if (_users is null)
            return request;

        if (!string.IsNullOrWhiteSpace(request.UserDisplayName) &&
            !string.IsNullOrWhiteSpace(request.UserEmail) &&
            !string.IsNullOrWhiteSpace(request.UserPhoneNumber))
        {
            return request;
        }

        var user = await _users.FindByIdAsync(request.UserId, ct).ConfigureAwait(false);
        if (user is null)
            return request;

        return request with
        {
            UserDisplayName = string.IsNullOrWhiteSpace(request.UserDisplayName)
                ? user.DisplayName
                : request.UserDisplayName,
            UserEmail = string.IsNullOrWhiteSpace(request.UserEmail)
                ? user.Email
                : request.UserEmail,
            UserPhoneNumber = string.IsNullOrWhiteSpace(request.UserPhoneNumber)
                ? user.PhoneNumber
                : request.UserPhoneNumber
        };
    }

    private async Task<UserTenantRoleAssignment> EnsureTenantMembershipAndGrantRolesCoreAsync(
        TenantMembershipRoleBootstrapRequest request,
        CancellationToken ct)
    {
        var result = await _roles.EnsureTenantMembershipAndGrantRolesAsync(request, ct).ConfigureAwait(false);
        var tenantName = string.IsNullOrWhiteSpace(request.TenantName) ? "Workspace" : request.TenantName.Trim();

        await _tenantExtensions.EnsureExtensionAsync(
            new IdentityTenant(
                request.TenantId,
                tenantName,
                IdentityTenant.NormalizeName(tenantName),
                IdentityTenantStatuses.Active),
            TenantExtensionContext.Create(
                TenantExtensionOperations.Linked,
                request.UserId),
            ct).ConfigureAwait(false);

        return result;
    }

    [IBeamOperation("identity.tenantroles.revoke")]
    public Task<UserTenantRoleAssignment> RevokeRolesAsync(Guid tenantId, Guid userId, IReadOnlyList<Guid> roleIds, CancellationToken ct = default)
        => _operations.ExecuteAsync(
            this,
            token => RevokeRolesCoreAsync(tenantId, userId, roleIds, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = userId },
            ct);

    private Task<UserTenantRoleAssignment> RevokeRolesCoreAsync(Guid tenantId, Guid userId, IReadOnlyList<Guid> roleIds, CancellationToken ct)
    {
        ValidateTenantId(tenantId);
        ValidateUserId(userId);
        ValidateRoleIds(roleIds);
        return _roles.RevokeRolesAsync(tenantId, userId, roleIds, ct);
    }

    [IBeamOperation("identity.tenantroles.user.list")]
    public Task<IReadOnlyList<TenantRole>> GetRolesForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
        => _operations.ExecuteAsync(
            this,
            token => GetRolesForUserCoreAsync(tenantId, userId, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = userId },
            ct);

    private Task<IReadOnlyList<TenantRole>> GetRolesForUserCoreAsync(Guid tenantId, Guid userId, CancellationToken ct)
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

    private static void ValidateBootstrapRoles(IReadOnlyList<Guid>? roleIds, IReadOnlyList<string>? roleNames)
    {
        if ((roleIds is null || roleIds.Count == 0) && (roleNames is null || roleNames.Count == 0))
            throw new IdentityValidationException("At least one roleId or roleName is required.");

        if (roleIds?.Any(x => x == Guid.Empty) == true)
            throw new IdentityValidationException("roleIds cannot contain empty GUID values.");

        if (roleNames?.Any(string.IsNullOrWhiteSpace) == true)
            throw new IdentityValidationException("roleNames cannot contain empty values.");
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
