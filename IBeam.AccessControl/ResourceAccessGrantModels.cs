namespace IBeam.AccessControl;

public sealed record ResourceAccessGrantRecord(
    Guid GrantId,
    Guid TenantId,
    string ResourceType,
    string ResourceId,
    AccessSubject Subject,
    string AccessLevel,
    string Status,
    DateTimeOffset CreatedUtc,
    Guid? CreatedByUserId,
    DateTimeOffset? UpdatedUtc,
    DateTimeOffset? ExpiresUtc,
    IReadOnlyDictionary<string, string> Metadata)
{
    public bool IsActive(DateTimeOffset now)
        => string.Equals(Status, ResourceAccessGrantStatuses.Active, StringComparison.OrdinalIgnoreCase) &&
           (ExpiresUtc is null || ExpiresUtc > now);
}

public sealed record ResourceAccessGrantInfo(
    Guid GrantId,
    Guid TenantId,
    string ResourceType,
    string ResourceId,
    AccessSubject Subject,
    string AccessLevel,
    string Status,
    DateTimeOffset CreatedUtc,
    Guid? CreatedByUserId,
    DateTimeOffset? UpdatedUtc,
    DateTimeOffset? ExpiresUtc,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static ResourceAccessGrantInfo FromRecord(ResourceAccessGrantRecord record)
        => new(
            record.GrantId,
            record.TenantId,
            record.ResourceType,
            record.ResourceId,
            record.Subject,
            record.AccessLevel,
            record.Status,
            record.CreatedUtc,
            record.CreatedByUserId,
            record.UpdatedUtc,
            record.ExpiresUtc,
            record.Metadata);
}

public sealed class GrantResourceAccessRequest
{
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public AccessSubject Subject { get; set; } = new(AccessSubjectTypes.User, string.Empty);
    public string AccessLevel { get; set; } = ResourceAccessLevels.View;
    public DateTimeOffset? ExpiresUtc { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
}

public sealed class UpdateResourceAccessGrantRequest
{
    public string? AccessLevel { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? ExpiresUtc { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

public sealed class CheckResourceAccessRequest
{
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public AccessSubject Subject { get; set; } = new(AccessSubjectTypes.User, string.Empty);
    public string RequiredAccessLevel { get; set; } = ResourceAccessLevels.View;
}

public static class ResourceAccessGrantStatuses
{
    public const string Active = "active";
    public const string Disabled = "disabled";
    public const string Revoked = "revoked";
}
