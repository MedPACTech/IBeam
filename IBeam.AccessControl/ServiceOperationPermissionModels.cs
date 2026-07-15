using System.Security.Claims;

namespace IBeam.AccessControl;

public sealed record ServiceOperationPermissionRule(
    Guid RuleId,
    Guid? TenantId,
    string Pattern,
    string Effect,
    IReadOnlyList<string> SubjectTypes,
    IReadOnlyList<string> RoleNames,
    IReadOnlyList<Guid> RoleIds,
    int Priority,
    string Source,
    string Status,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    Guid? UpdatedByUserId)
{
    public bool IsActive =>
        string.Equals(Status, ServiceOperationPermissionStatuses.Active, StringComparison.OrdinalIgnoreCase);
}

public sealed record ServiceOperationPermissionInfo(
    Guid RuleId,
    Guid? TenantId,
    string Pattern,
    string Effect,
    IReadOnlyList<string> SubjectTypes,
    IReadOnlyList<string> RoleNames,
    IReadOnlyList<Guid> RoleIds,
    int Priority,
    string Source,
    string Status,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    Guid? UpdatedByUserId)
{
    public static ServiceOperationPermissionInfo FromRecord(ServiceOperationPermissionRule record)
        => new(
            record.RuleId,
            record.TenantId,
            record.Pattern,
            record.Effect,
            record.SubjectTypes,
            record.RoleNames,
            record.RoleIds,
            record.Priority,
            record.Source,
            record.Status,
            record.CreatedUtc,
            record.UpdatedUtc,
            record.UpdatedByUserId);
}

public sealed class UpsertServiceOperationPermissionRequest
{
    public Guid? RuleId { get; set; }
    public string Pattern { get; set; } = string.Empty;
    public string Effect { get; set; } = ServiceOperationPermissionEffects.Allow;
    public List<string> SubjectTypes { get; set; } = [];
    public List<string> RoleNames { get; set; } = [];
    public List<Guid> RoleIds { get; set; } = [];
    public int Priority { get; set; } = 0;
}

public sealed class CheckServiceOperationAccessRequest
{
    public string OperationName { get; set; } = string.Empty;
}

public sealed record ServiceOperationAuthorizationRequest(
    Guid TenantId,
    ClaimsPrincipal Principal,
    string OperationName);

public sealed record ServiceOperationAuthorizationResult(
    bool Allowed,
    string OperationName,
    string Reason,
    ServiceOperationPermissionInfo? MatchedRule = null)
{
    public static ServiceOperationAuthorizationResult Allow(
        string operationName,
        string reason,
        ServiceOperationPermissionInfo? matchedRule = null)
        => new(true, operationName, reason, matchedRule);

    public static ServiceOperationAuthorizationResult Deny(
        string operationName,
        string reason,
        ServiceOperationPermissionInfo? matchedRule = null)
        => new(false, operationName, reason, matchedRule);
}

public sealed class ServiceOperationAuthorizationOptions
{
    public const string SectionName = "IBeam:Services:Authorization";

    public bool Enabled { get; set; } = false;

    public string DefaultMode { get; set; } = ServiceOperationAuthorizationDefaultModes.RequirePermission;

    public List<ServiceOperationPermissionRuleOptions> Rules { get; set; } = [];

    public List<ServiceOperationPermissionRuleOptions> EmergencyOverrides { get; set; } = [];

    public void Validate()
    {
        DefaultMode = NormalizeDefaultMode(DefaultMode);
        Rules = Rules.Where(x => !string.IsNullOrWhiteSpace(x.Pattern)).ToList();
        EmergencyOverrides = EmergencyOverrides.Where(x => !string.IsNullOrWhiteSpace(x.Pattern)).ToList();
    }

    private static string NormalizeDefaultMode(string value)
        => string.Equals(value, ServiceOperationAuthorizationDefaultModes.Allow, StringComparison.OrdinalIgnoreCase)
            ? ServiceOperationAuthorizationDefaultModes.Allow
            : ServiceOperationAuthorizationDefaultModes.RequirePermission;
}

public sealed class ServiceOperationPermissionRuleOptions
{
    public Guid? TenantId { get; set; }
    public string Pattern { get; set; } = string.Empty;
    public string Effect { get; set; } = ServiceOperationPermissionEffects.Allow;
    public List<string> SubjectTypes { get; set; } = [];
    public List<string> RoleNames { get; set; } = [];
    public List<Guid> RoleIds { get; set; } = [];
    public int Priority { get; set; } = 0;
}

public static class ServiceOperationPermissionEffects
{
    public const string Allow = "allow";
    public const string Deny = "deny";
}

public static class ServiceOperationPermissionStatuses
{
    public const string Active = "active";
    public const string Disabled = "disabled";
}

public static class ServiceOperationPermissionSources
{
    public const string Configuration = "configuration";
    public const string EmergencyConfiguration = "configuration-emergency";
    public const string Store = "store";
}

public static class ServiceOperationAuthorizationDefaultModes
{
    public const string Allow = "allow";
    public const string RequirePermission = "require-permission";
}
