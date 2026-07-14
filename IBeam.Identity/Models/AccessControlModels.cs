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

public static class AccessCatalogSources
{
    public const string IBeamDefault = "ibeamDefault";
    public const string HostConfig = "hostConfig";
    public const string HostProvider = "hostProvider";
    public const string TenantDb = "tenantDb";
    public const string TenantOverride = "tenantOverride";
}

public static class AccessCatalogCategories
{
    public const string Role = "role";
    public const string Permission = "permission";
    public const string Operation = "operation";
    public const string Module = "module";
    public const string ApiScope = "apiScope";
    public const string Tool = "tool";
    public const string Agent = "agent";
    public const string Resource = "resource";
    public const string AccessLevel = "accessLevel";
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
    IReadOnlyList<string>? SupportedAccessLevels = null,
    string? ParentResourceType = null,
    string? ParentResourceId = null,
    string Source = AccessCatalogSources.HostProvider,
    bool IsAssignable = true,
    bool IsMutable = false,
    bool IsEnabled = true);

public sealed record AccessCatalogItem(
    string Key,
    string Label,
    string? Description,
    string Category,
    string Source,
    bool IsAssignable,
    bool IsMutable,
    bool IsEnabled,
    IReadOnlyList<string>? SubjectTypes = null,
    string? ResourceType = null,
    string? ResourceId = null,
    string? ParentResourceType = null,
    string? ParentResourceId = null,
    IReadOnlyList<string>? SupportedAccessLevels = null,
    int? Rank = null,
    string? ModuleKey = null,
    string? RequiredAccessLevel = null,
    bool IsDangerous = false,
    string? IdParameter = null);

public sealed record AccessOperationCatalogItem(
    string Key,
    string Label,
    string? Description,
    string? ModuleKey,
    string? ResourceType,
    string? RequiredAccessLevel,
    string Category,
    bool IsAssignable,
    bool IsDangerous,
    string Source,
    string? DeclaringType = null,
    string? MethodName = null,
    string? IdParameter = null);

public sealed record AccessCatalogDto(
    IReadOnlyList<AccessCatalogItem> Roles,
    IReadOnlyList<AccessCatalogItem> Permissions,
    IReadOnlyList<AccessCatalogItem> Operations,
    IReadOnlyList<AccessCatalogItem> Modules,
    IReadOnlyList<AccessCatalogItem> ApiScopes,
    IReadOnlyList<AccessCatalogItem> Tools,
    IReadOnlyList<AccessCatalogItem> Agents,
    IReadOnlyList<AccessCatalogItem> Resources,
    IReadOnlyList<AccessCatalogItem> AccessLevels,
    IReadOnlyList<string>? ResourceTypes = null);

public sealed record AccessCatalogOverride(
    Guid CatalogItemId,
    Guid TenantId,
    string Key,
    string Label,
    string? Description,
    string Category,
    bool IsAssignable,
    bool IsMutable,
    bool IsEnabled,
    IReadOnlyList<string>? SubjectTypes = null,
    string? ResourceType = null,
    string? ResourceId = null,
    string? ParentResourceType = null,
    string? ParentResourceId = null,
    IReadOnlyList<string>? SupportedAccessLevels = null,
    int? Rank = null,
    string? ModuleKey = null,
    string? RequiredAccessLevel = null,
    bool IsDangerous = false,
    string? IdParameter = null,
    DateTimeOffset? CreatedAt = null,
    DateTimeOffset? UpdatedAt = null);

public sealed class UpsertAccessCatalogOverrideRequest
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = AccessCatalogCategories.Resource;
    public bool IsAssignable { get; set; } = true;
    public bool IsMutable { get; set; } = true;
    public bool IsEnabled { get; set; } = true;
    public List<string> SubjectTypes { get; set; } = [];
    public string? ResourceType { get; set; }
    public string? ResourceId { get; set; }
    public string? ParentResourceType { get; set; }
    public string? ParentResourceId { get; set; }
    public List<string> SupportedAccessLevels { get; set; } = [];
    public int? Rank { get; set; }
    public string? ModuleKey { get; set; }
    public string? RequiredAccessLevel { get; set; }
    public bool IsDangerous { get; set; }
    public string? IdParameter { get; set; }
}

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
    string AccessLevel = AccessLevels.View,
    string? Module = null,
    string? Permission = null,
    string? AgentKey = null,
    string? MinimumAccessLevel = null);

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
