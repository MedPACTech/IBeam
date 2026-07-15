namespace IBeam.AccessControl;

public interface IResourceAccessStore
{
    Task<IReadOnlyList<ResourceAccessGrantRecord>> ListGrantsAsync(Guid tenantId, CancellationToken ct = default);
    Task<ResourceAccessGrantRecord?> GetGrantAsync(Guid tenantId, Guid grantId, CancellationToken ct = default);
    Task<ResourceAccessGrantRecord> UpsertGrantAsync(ResourceAccessGrantRecord grant, CancellationToken ct = default);
    Task DeleteGrantAsync(Guid tenantId, Guid grantId, CancellationToken ct = default);
}

public interface IResourceAccessHierarchyResolver
{
    Task<IReadOnlyList<ResourceAccessResource>> ListAncestorsAsync(
        Guid tenantId,
        string resourceType,
        string resourceId,
        CancellationToken ct = default);
}

public interface IResourceAccessService
{
    Task<IReadOnlyList<ResourceAccessGrantInfo>> ListGrantsAsync(
        Guid tenantId,
        string? resourceType = null,
        string? resourceId = null,
        AccessSubject? subject = null,
        CancellationToken ct = default);

    Task<ResourceAccessGrantInfo> GrantAccessAsync(
        Guid tenantId,
        GrantResourceAccessRequest request,
        Guid? createdByUserId = null,
        CancellationToken ct = default);

    Task<ResourceAccessGrantInfo> UpdateGrantAsync(
        Guid tenantId,
        Guid grantId,
        UpdateResourceAccessGrantRequest request,
        CancellationToken ct = default);

    Task RevokeGrantAsync(Guid tenantId, Guid grantId, CancellationToken ct = default);
}

public interface IResourceAccessAuthorizer
{
    Task<ResourceAccessAuthorizationResult> AuthorizeAsync(
        Guid tenantId,
        string resourceType,
        string resourceId,
        AccessSubject subject,
        string requiredAccessLevel,
        CancellationToken ct = default);
}

public interface IPermissionRoleMapStore
{
    Task<PermissionGrantSet> ResolveGrantsAsync(
        Guid tenantId,
        IReadOnlyList<string> permissionNames,
        IReadOnlyList<Guid> permissionIds,
        CancellationToken ct = default);

    Task<IReadOnlyList<PermissionRoleMapRecord>> ListMappingsAsync(Guid tenantId, CancellationToken ct = default);

    Task<PermissionRoleMapRecord> UpsertByPermissionNameAsync(
        Guid tenantId,
        string permissionName,
        IReadOnlyList<string> roleNames,
        IReadOnlyList<Guid> roleIds,
        CancellationToken ct = default);

    Task<PermissionRoleMapRecord> UpsertByPermissionIdAsync(
        Guid tenantId,
        Guid permissionId,
        IReadOnlyList<string> roleNames,
        IReadOnlyList<Guid> roleIds,
        CancellationToken ct = default);

    Task DeleteByPermissionNameAsync(Guid tenantId, string permissionName, CancellationToken ct = default);
    Task DeleteByPermissionIdAsync(Guid tenantId, Guid permissionId, CancellationToken ct = default);
}

public interface IPermissionRoleMapService
{
    Task<IReadOnlyList<PermissionRoleMapInfo>> ListMappingsAsync(Guid tenantId, CancellationToken ct = default);
    Task<PermissionRoleMapInfo> UpsertByPermissionNameAsync(Guid tenantId, string permissionName, UpsertPermissionRoleMapRequest request, CancellationToken ct = default);
    Task<PermissionRoleMapInfo> UpsertByPermissionIdAsync(Guid tenantId, Guid permissionId, UpsertPermissionRoleMapRequest request, CancellationToken ct = default);
    Task DeleteByPermissionNameAsync(Guid tenantId, string permissionName, CancellationToken ct = default);
    Task DeleteByPermissionIdAsync(Guid tenantId, Guid permissionId, CancellationToken ct = default);
}

public interface IPermissionRoleAuthorizer
{
    Task<bool> AuthorizeAsync(
        Guid tenantId,
        System.Security.Claims.ClaimsPrincipal principal,
        IReadOnlyList<string> permissionNames,
        IReadOnlyList<Guid> permissionIds,
        CancellationToken ct = default);
}

public interface IServiceOperationPermissionStore
{
    Task<IReadOnlyList<ServiceOperationPermissionRule>> ListRulesAsync(Guid tenantId, CancellationToken ct = default);

    Task<ServiceOperationPermissionRule?> GetRuleAsync(Guid tenantId, Guid ruleId, CancellationToken ct = default);

    Task<ServiceOperationPermissionRule> UpsertRuleAsync(ServiceOperationPermissionRule rule, CancellationToken ct = default);

    Task DisableRuleAsync(Guid tenantId, Guid ruleId, Guid? updatedByUserId = null, CancellationToken ct = default);

    Task DeleteRuleAsync(Guid tenantId, Guid ruleId, CancellationToken ct = default);
}

public interface IServiceOperationPermissionRuleProvider
{
    Task<IReadOnlyList<ServiceOperationPermissionRule>> ListRulesAsync(Guid tenantId, CancellationToken ct = default);
}

public interface IServiceOperationPermissionService
{
    Task<IReadOnlyList<ServiceOperationPermissionInfo>> ListRulesAsync(Guid tenantId, CancellationToken ct = default);

    Task<ServiceOperationPermissionInfo> UpsertRuleAsync(
        Guid tenantId,
        UpsertServiceOperationPermissionRequest request,
        Guid? updatedByUserId = null,
        CancellationToken ct = default);

    Task DisableRuleAsync(Guid tenantId, Guid ruleId, Guid? updatedByUserId = null, CancellationToken ct = default);

    Task DeleteRuleAsync(Guid tenantId, Guid ruleId, CancellationToken ct = default);
}

public interface IServiceOperationAuthorizer
{
    Task<ServiceOperationAuthorizationResult> AuthorizeAsync(
        ServiceOperationAuthorizationRequest request,
        CancellationToken ct = default);
}
