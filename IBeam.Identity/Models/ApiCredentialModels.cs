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
    DateTimeOffset? RevokedUtc,
    Guid? RevokedByUserId,
    string? RevocationReason,
    bool IsDeleted)
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
    DateTimeOffset? RevokedUtc,
    Guid? RevokedByUserId,
    string? RevocationReason,
    bool IsDeleted)
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
            record.RevokedUtc,
            record.RevokedByUserId,
            record.RevocationReason,
            record.IsDeleted);
}

public sealed class CreateApiCredentialRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public string? AgentKey { get; set; }
    public List<string> RoleNames { get; set; } = [];
    public List<Guid> RoleIds { get; set; } = [];
    public DateTimeOffset? ExpiresUtc { get; set; }
}

public sealed class CreateApiCredentialResult
{
    public ApiCredentialInfo Credential { get; set; } = default!;
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class UpdateApiCredentialRolesRequest
{
    public List<string> RoleNames { get; set; } = [];
    public List<Guid> RoleIds { get; set; } = [];
}

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
