using System.Security.Claims;

namespace IBeam.Identity.Models;

public sealed record ApiCredentialRecord(
    Guid CredentialId,
    Guid TenantId,
    string DisplayName,
    string? AgentKey,
    string KeyPrefix,
    string SecretHash,
    IReadOnlyList<string> RoleNames,
    IReadOnlyList<Guid> RoleIds,
    DateTimeOffset CreatedUtc,
    Guid? CreatedByUserId,
    DateTimeOffset? ExpiresUtc,
    DateTimeOffset? LastUsedUtc,
    string? LastUsedIp,
    DateTimeOffset? RotatedUtc,
    DateTimeOffset? RevokedUtc,
    Guid? RevokedByUserId,
    string? RevocationReason,
    bool IsDeleted,
    string? Description = null,
    string? AgentDisplayName = null,
    IReadOnlyList<string>? AllowedAgentKeys = null)
{
    public bool IsActive(DateTimeOffset now) =>
        !IsDeleted &&
        RevokedUtc is null &&
        (ExpiresUtc is null || ExpiresUtc > now);
}

public sealed record ApiCredentialInfo(
    Guid Id,
    Guid TenantId,
    string DisplayName,
    string? AgentKey,
    IReadOnlyList<string> RoleNames,
    IReadOnlyList<Guid> RoleIds,
    string KeyPrefix,
    DateTimeOffset CreatedUtc,
    Guid? CreatedByUserId,
    DateTimeOffset? ExpiresUtc,
    DateTimeOffset? LastUsedUtc,
    string? LastUsedIp,
    DateTimeOffset? RotatedUtc,
    DateTimeOffset? RevokedUtc,
    Guid? RevokedByUserId,
    string? RevocationReason,
    bool IsDeleted,
    string? Description = null,
    string? AgentDisplayName = null,
    IReadOnlyList<string>? AllowedAgentKeys = null)
{
    public bool IsActive => !IsDeleted && RevokedUtc is null && (ExpiresUtc is null || ExpiresUtc > DateTimeOffset.UtcNow);

    public static ApiCredentialInfo FromRecord(ApiCredentialRecord record) =>
        new(
            record.CredentialId,
            record.TenantId,
            record.DisplayName,
            record.AgentKey,
            record.RoleNames,
            record.RoleIds,
            record.KeyPrefix,
            record.CreatedUtc,
            record.CreatedByUserId,
            record.ExpiresUtc,
            record.LastUsedUtc,
            record.LastUsedIp,
            record.RotatedUtc,
            record.RevokedUtc,
            record.RevokedByUserId,
            record.RevocationReason,
            record.IsDeleted,
            record.Description,
            record.AgentDisplayName,
            record.AllowedAgentKeys ?? Array.Empty<string>());
}

public sealed class CreateApiCredentialRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? AgentKey { get; set; }
    public string? AgentDisplayName { get; set; }
    public List<string> AllowedAgentKeys { get; set; } = [];
    public List<string> RoleNames { get; set; } = [];
    public List<Guid> RoleIds { get; set; } = [];
    public DateTimeOffset? ExpiresUtc { get; set; }
}

public sealed class CreateApiCredentialResult
{
    public ApiCredentialInfo Credential { get; set; } = default!;
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class UpdateApiCredentialRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? AgentKey { get; set; }
    public string? AgentDisplayName { get; set; }
    public List<string> AllowedAgentKeys { get; set; } = [];
    public List<string> RoleNames { get; set; } = [];
    public List<Guid> RoleIds { get; set; } = [];
    public DateTimeOffset? ExpiresUtc { get; set; }
}

public sealed class UpdateApiCredentialRolesRequest
{
    public List<string> RoleNames { get; set; } = [];
    public List<Guid> RoleIds { get; set; } = [];
}

public sealed class UpdateApiCredentialAccessRequest
{
    public List<string> RoleNames { get; set; } = [];
    public List<Guid> RoleIds { get; set; } = [];
    public List<string> ApiScopes { get; set; } = [];
    public List<string> ToolScopes { get; set; } = [];
    public List<string> AllowedAgentKeys { get; set; } = [];
}

public sealed record ApiCredentialRoleCatalogEntry(
    string Name,
    string DisplayName,
    string Description,
    string Category,
    bool IsBuiltIn,
    bool IsPattern,
    bool IsAssignable);

public sealed record ApiScopeCatalogItem(
    string Key,
    string DisplayName,
    string Description,
    string Category,
    bool IsAssignable,
    bool IsWildcardCapable,
    string? RequiredParentScope = null,
    string? ModuleKey = null,
    string? ResourceType = null);

public sealed record AgentCatalogItem(
    string Key,
    string DisplayName,
    string? Description = null,
    bool IsAssignable = true);

public sealed record ApiCredentialContext(
    Guid TenantId,
    Guid CredentialId,
    string CredentialName,
    string? AgentKey,
    bool IsActive,
    IReadOnlyList<string> Roles,
    IReadOnlyList<Guid> RoleIds);

public sealed record ApiCredentialAccessContextDto(
    string PrincipalType,
    Guid TenantId,
    Guid CredentialId,
    string CredentialName,
    string? AgentKey,
    string? AgentDisplayName,
    bool IsActive,
    IReadOnlyList<string> Roles,
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string> ApiScopes,
    IReadOnlyList<string> Tools,
    IReadOnlyList<string> AllowedAgentKeys,
    IReadOnlyDictionary<string, IReadOnlyList<ApiCredentialResourceAccessDto>> Resources,
    ApiCredentialAccessCapabilitiesDto Capabilities);

public sealed record ApiCredentialResourceAccessDto(
    string ResourceId,
    string? Slug,
    string AccessLevel,
    string Source);

public sealed record ApiCredentialAccessCapabilitiesDto(
    bool CanUseMcp,
    bool CanAccessWorkApi,
    bool CanActAsRequestedAgent);

public sealed record ApiCredentialAccessEvaluationContext(
    Guid TenantId,
    ApiCredentialInfo Credential,
    ApiCredentialAccessContextDto AccessContext,
    string? RequestedAgentKey,
    string? ResourceType,
    string? ResourceId,
    string? MinimumAccessLevel);

public sealed class RevokeApiCredentialRequest
{
    public string? Reason { get; set; }
}

public sealed class ApiCredentialIntrospectionRequest
{
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class ApiCredentialIntrospectionResult
{
    public bool Active { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? CredentialId { get; set; }
    public string? DisplayName { get; set; }
    public string? AgentKey { get; set; }
    public IReadOnlyList<string> RoleNames { get; set; } = [];
    public IReadOnlyList<Guid> RoleIds { get; set; } = [];
    public DateTimeOffset? ExpiresUtc { get; set; }
    public string ApiSubjectType { get; set; } = "credential";
    public string? FailureReason { get; set; }
}

public sealed record ParsedApiCredentialKey(
    string Prefix,
    Guid TenantId,
    Guid CredentialId,
    string Secret);

public sealed record ApiCredentialAuthenticationResult(
    bool Succeeded,
    ApiCredentialRecord? Credential = null,
    ClaimsPrincipal? Principal = null,
    string? FailureReason = null)
{
    public static ApiCredentialAuthenticationResult Fail(string reason) => new(false, FailureReason: reason);
    public static ApiCredentialAuthenticationResult Success(ApiCredentialRecord credential, ClaimsPrincipal principal)
        => new(true, credential, principal);
}
