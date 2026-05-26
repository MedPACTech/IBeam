namespace IBeam.Identity.Models;

public sealed record PermissionGrantSet(
    IReadOnlyList<string> RoleNames,
    IReadOnlyList<Guid> RoleIds)
{
    public static PermissionGrantSet Empty { get; } = new(Array.Empty<string>(), Array.Empty<Guid>());
    public bool HasAnyGrant => RoleNames.Count > 0 || RoleIds.Count > 0;
}
