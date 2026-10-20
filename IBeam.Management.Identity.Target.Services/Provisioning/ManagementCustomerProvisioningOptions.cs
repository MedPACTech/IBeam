namespace IBeam.Management.Identity.Target.Services.Provisioning;
public sealed class ManagementCustomerProvisioningOptions
{
    public const string SectionName = "IBeam:Management:CustomerIdentity:Provisioning";
    public Dictionary<string, string[]> Policies { get; set; } = new(StringComparer.Ordinal);
    public bool IsValid() => Policies is { Count: > 0 and <= 16 } && Policies.All(x => x.Key.Length is > 0 and <= 64 &&
        x.Key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') && x.Value is { Length: > 0 and <= 8 } &&
        x.Value.Distinct(StringComparer.Ordinal).Count() == x.Value.Length && x.Value.All(ValidRoleName)) &&
        Policies.Values.SelectMany(x => x).Distinct(StringComparer.Ordinal).Count() <= 32;
    public static bool ValidRoleName(string name) => !string.IsNullOrWhiteSpace(name) && name == name.Trim() && name.Length <= 100 &&
        !name.Any(char.IsControl) && !name.Contains('*') && !name.Contains('?') && !name.Contains(',') &&
        !name.StartsWith("support", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("management.", StringComparison.OrdinalIgnoreCase);
}
