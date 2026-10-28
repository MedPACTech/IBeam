namespace IBeam.Identity.Options;

/// <summary>Explicit host policy. Role names never come from a caller or a legacy tenant owner column.</summary>
public sealed class IdentityTenantOwnershipOptions
{
    public const string SectionName = "IBeam:Identity:TenantOwnership";
    public bool Enabled { get; set; }
    public string[] OwnerRoleNames { get; set; } = [];
    public bool IsValid() => OwnerRoleNames is not null && (!Enabled ? OwnerRoleNames.Length == 0 :
        OwnerRoleNames.Length is > 0 and <= 8 && OwnerRoleNames.All(x => !string.IsNullOrWhiteSpace(x) && x == x.Trim() &&
            x.Length <= 100 && !x.Any(char.IsControl) && x.IndexOfAny([',', '*', '?']) < 0) &&
        OwnerRoleNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() == OwnerRoleNames.Length);
}
