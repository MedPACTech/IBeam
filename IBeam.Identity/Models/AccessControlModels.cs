using System.Security.Claims;

namespace IBeam.Identity.Models;

public static class AccessSubjectTypes
{
    public const string User = "user";
    public const string Group = "group";
    public const string Team = "team";
    public const string ApiCredential = "apiCredential";
}

public static class AccessResourceTypes
{
    public const string Module = "module";
}

public static class AccessLevels
{
    public const string View = "view";
    public const string Edit = "edit";
    public const string Manage = "manage";
}

public sealed record AccessModuleDefinition(
    string Key,
    string Label,
    string? Description = null,
    IReadOnlyList<string>? SupportedAccessLevels = null,
    IReadOnlyList<string>? ImpliedByRoleNames = null,
    IReadOnlyList<Guid>? ImpliedByRoleIds = null,
    IReadOnlyList<string>? ImpliedByPermissionNames = null,
    IReadOnlyList<Guid>? ImpliedByPermissionIds = null);

public sealed record AccessLevelDefinition(
    string Key,
    int Rank,
    string? Label = null);

public sealed record AccessCatalogResource(
    string ResourceType,
    string ResourceId,
    string Label,
    string? Description = null,
    IReadOnlyList<string>? SupportedAccessLevels = null);

public sealed record AccessCatalogDto(
    IReadOnlyList<string> ResourceTypes,
    IReadOnlyList<string> AccessLevels,
    IReadOnlyList<AccessCatalogResource> Resources);

public sealed record AccessGrant(
    Guid GrantId,
    Guid TenantId,
    string SubjectType,
    string SubjectId,
    string ResourceType,
    string ResourceId,
    string AccessLevel,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt = null);

public sealed record AccessCheckRequest(
    string SubjectType,
    string SubjectId,
    string ResourceType,
    string ResourceId,
    string AccessLevel = AccessLevels.View);

public sealed record AccessDecision(
    bool IsAllowed,
    string Source,
    string? Reason = null,
    string? AccessLevel = null);

public sealed record AccessEvaluationContext(
    Guid TenantId,
    ClaimsPrincipal Principal,
    string SubjectType,
    string SubjectId,
    string ResourceType,
    string ResourceId,
    string AccessLevel,
    IReadOnlyList<string> RoleNames,
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<string> PermissionNames,
    IReadOnlyList<AccessGrant> Grants);

public sealed record AccessContextDto(
    string UserId,
    Guid TenantId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<AccessContextModuleDto> Modules,
    IReadOnlyDictionary<string, IReadOnlyList<AccessContextResourceDto>> Resources,
    AccessCapabilitiesDto Capabilities);

public sealed record AccessContextModuleDto(
    string Module,
    string AccessLevel,
    string Source);

public sealed record AccessContextResourceDto(
    string ResourceId,
    string AccessLevel,
    string Source);

public sealed record AccessCapabilitiesDto(
    bool CanManageUsers,
    bool CanManageRoles,
    bool CanManageAccess,
    bool CanAssignOwner);

