using System.Security.Claims;

namespace IBeam.Identity.Models;

public static class AgentUserStatuses
{
    public const string Active = "active";
    public const string Disabled = "disabled";
    public const string Archived = "archived";
}

public static class AgentUserClaimTypes
{
    public const string AgentUserId = "agent_user_id";
    public const string AgentUserName = "agent_user_name";
    public const string AgentType = "agent_type";
    public const string AgentKey = "agent_key";
}

public sealed record AgentUserRecord(
    Guid AgentUserId,
    Guid TenantId,
    string DisplayName,
    string? Description,
    string AgentType,
    string AgentKey,
    string Status,
    DateTimeOffset CreatedUtc,
    Guid? CreatedByUserId,
    DateTimeOffset? UpdatedUtc,
    string? MetadataJson)
{
    public bool IsActive => string.Equals(Status, AgentUserStatuses.Active, StringComparison.OrdinalIgnoreCase);
}

public sealed record AgentUserCredentialBindingRecord(
    Guid BindingId,
    Guid TenantId,
    Guid AgentUserId,
    Guid CredentialId,
    string? Purpose,
    string? EnvironmentKey,
    string Status,
    DateTimeOffset CreatedUtc,
    Guid? CreatedByUserId,
    DateTimeOffset? RevokedUtc,
    Guid? RevokedByUserId,
    string? MetadataJson)
{
    public bool IsActive => string.Equals(Status, AgentUserStatuses.Active, StringComparison.OrdinalIgnoreCase) && RevokedUtc is null;
}

public sealed record AgentUserInfo(
    Guid Id,
    Guid TenantId,
    string DisplayName,
    string? Description,
    string AgentType,
    string AgentKey,
    string Status,
    DateTimeOffset CreatedUtc,
    Guid? CreatedByUserId,
    DateTimeOffset? UpdatedUtc,
    string? MetadataJson)
{
    public bool IsActive => string.Equals(Status, AgentUserStatuses.Active, StringComparison.OrdinalIgnoreCase);

    public static AgentUserInfo FromRecord(AgentUserRecord record)
        => new(
            record.AgentUserId,
            record.TenantId,
            record.DisplayName,
            record.Description,
            record.AgentType,
            record.AgentKey,
            record.Status,
            record.CreatedUtc,
            record.CreatedByUserId,
            record.UpdatedUtc,
            record.MetadataJson);
}

public sealed record AgentUserCredentialBindingInfo(
    Guid Id,
    Guid TenantId,
    Guid AgentUserId,
    Guid CredentialId,
    string? Purpose,
    string? EnvironmentKey,
    string Status,
    DateTimeOffset CreatedUtc,
    Guid? CreatedByUserId,
    DateTimeOffset? RevokedUtc,
    Guid? RevokedByUserId,
    string? MetadataJson)
{
    public bool IsActive => string.Equals(Status, AgentUserStatuses.Active, StringComparison.OrdinalIgnoreCase) && RevokedUtc is null;

    public static AgentUserCredentialBindingInfo FromRecord(AgentUserCredentialBindingRecord record)
        => new(
            record.BindingId,
            record.TenantId,
            record.AgentUserId,
            record.CredentialId,
            record.Purpose,
            record.EnvironmentKey,
            record.Status,
            record.CreatedUtc,
            record.CreatedByUserId,
            record.RevokedUtc,
            record.RevokedByUserId,
            record.MetadataJson);
}

public sealed class CreateAgentUserRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string AgentType { get; set; } = "custom";
    public string AgentKey { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }
}

public sealed class UpdateAgentUserRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string AgentType { get; set; } = "custom";
    public string AgentKey { get; set; } = string.Empty;
    public string Status { get; set; } = AgentUserStatuses.Active;
    public string? MetadataJson { get; set; }
}

public sealed class BindAgentUserCredentialRequest
{
    public Guid CredentialId { get; set; }
    public string? Purpose { get; set; }
    public string? EnvironmentKey { get; set; }
    public string? MetadataJson { get; set; }
}

public sealed record AgentUserMeDto(
    string PrincipalType,
    Guid TenantId,
    Guid AgentUserId,
    string DisplayName,
    string AgentType,
    string AgentKey,
    string? Description,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<string> Tools);

public sealed record ResolvedAgentUser(
    AgentUserInfo AgentUser,
    AgentUserCredentialBindingInfo Binding);

public static class AgentUserClaimsPrincipalExtensions
{
    public static Guid? GetAgentUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirst(AgentUserClaimTypes.AgentUserId)?.Value;
        return Guid.TryParse(value, out var parsed) && parsed != Guid.Empty ? parsed : null;
    }
}
