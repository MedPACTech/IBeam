namespace IBeam.AccessControl;

public sealed record PermissionRoleMapRecord(
    Guid TenantId,
    string? PermissionName,
    Guid? PermissionId,
    IReadOnlyList<string> RoleNames,
    IReadOnlyList<Guid> RoleIds,
    string Status,
    DateTimeOffset UpdatedUtc)
{
    public bool IsActive =>
        string.Equals(Status, PermissionRoleMapStatuses.Active, StringComparison.OrdinalIgnoreCase);
}

public sealed record PermissionRoleMapInfo(
    Guid TenantId,
    string? PermissionName,
    Guid? PermissionId,
    IReadOnlyList<string> RoleNames,
    IReadOnlyList<Guid> RoleIds,
    string Status,
    DateTimeOffset UpdatedUtc)
{
    public static PermissionRoleMapInfo FromRecord(PermissionRoleMapRecord record)
        => new(
            record.TenantId,
            record.PermissionName,
            record.PermissionId,
            record.RoleNames,
            record.RoleIds,
            record.Status,
            record.UpdatedUtc);
}

public sealed record PermissionGrantSet(
    IReadOnlyList<string> RoleNames,
    IReadOnlyList<Guid> RoleIds)
{
    public static PermissionGrantSet Empty { get; } = new(Array.Empty<string>(), Array.Empty<Guid>());
    public bool HasAnyGrant => RoleNames.Count > 0 || RoleIds.Count > 0;
}

public sealed class UpsertPermissionRoleMapRequest
{
    public string? PermissionName { get; set; }
    public Guid? PermissionId { get; set; }
    public List<string> RoleNames { get; set; } = [];
    public List<Guid> RoleIds { get; set; } = [];
}

public sealed class CheckPermissionAccessRequest
{
    public string PermissionName { get; set; } = string.Empty;
    public Guid? PermissionId { get; set; }
}

public static class PermissionRoleMapStatuses
{
    public const string Active = "active";
    public const string Disabled = "disabled";
}
