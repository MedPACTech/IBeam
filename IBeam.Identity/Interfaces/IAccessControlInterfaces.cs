using System.Security.Claims;
using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface IIBeamAccessCatalogProvider
{
    Task<IReadOnlyList<AccessCatalogResource>> GetResourcesAsync(Guid tenantId, CancellationToken ct = default);
}

public interface IIBeamAccessRuleProvider
{
    Task<IReadOnlyList<AccessDecision>> EvaluateAsync(AccessEvaluationContext context, CancellationToken ct = default);
}

public interface IIBeamAccessGrantStore
{
    Task<IReadOnlyList<AccessGrant>> GetGrantsAsync(
        Guid tenantId,
        string? subjectType = null,
        string? subjectId = null,
        CancellationToken ct = default);

    Task<AccessGrant?> GetGrantAsync(Guid tenantId, Guid grantId, CancellationToken ct = default);

    Task<AccessGrant> UpsertGrantAsync(
        Guid tenantId,
        Guid? grantId,
        string subjectType,
        string subjectId,
        string resourceType,
        string resourceId,
        string accessLevel,
        CancellationToken ct = default);

    Task DeleteGrantAsync(Guid tenantId, Guid grantId, CancellationToken ct = default);
}

public interface IIBeamAccessControlService
{
    Task<bool> HasRoleAsync(ClaimsPrincipal principal, string roleName, CancellationToken ct = default);
    Task<bool> HasPermissionAsync(ClaimsPrincipal principal, string permissionName, CancellationToken ct = default);
    Task<bool> HasModuleAccessAsync(ClaimsPrincipal principal, string moduleKey, string minimumAccessLevel = AccessLevels.View, CancellationToken ct = default);
    Task<bool> HasResourceAccessAsync(ClaimsPrincipal principal, string resourceType, string resourceId, string minimumAccessLevel = AccessLevels.View, CancellationToken ct = default);

    Task RequirePermissionAsync(ClaimsPrincipal principal, string permissionName, CancellationToken ct = default);
    Task RequireModuleAccessAsync(ClaimsPrincipal principal, string moduleKey, string minimumAccessLevel = AccessLevels.View, CancellationToken ct = default);
    Task RequireResourceAccessAsync(ClaimsPrincipal principal, string resourceType, string resourceId, string minimumAccessLevel = AccessLevels.View, CancellationToken ct = default);

    Task<AccessCatalogDto> GetAccessCatalogAsync(Guid tenantId, CancellationToken ct = default);
    Task<AccessContextDto> GetCurrentAccessContextAsync(ClaimsPrincipal principal, Guid? tenantId = null, CancellationToken ct = default);
    Task<AccessDecision> CheckAccessAsync(ClaimsPrincipal principal, Guid tenantId, AccessCheckRequest request, CancellationToken ct = default);
}

