namespace IBeam.Management.Identity.Target.Services.Onboarding;

public sealed class ManagementEnterpriseOnboardingOptions
{
    public const string SectionName = "IBeam:Management:CustomerIdentity:Onboarding";
    /// <summary>Host-owned keys mapped to an existing target-local administrator role seeded by lifecycle hooks.</summary>
    public Dictionary<string, string> Policies { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> DisabledPolicyKeys { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Initializers { get; set; } = new(StringComparer.Ordinal);
    public bool IsValid() => Policies is { Count: > 0 and <= 32 } && Policies.All(x =>
        x.Key.Length is > 0 and <= 64 && x.Key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') &&
        Provisioning.ManagementCustomerProvisioningOptions.ValidRoleName(x.Value)) &&
        DisabledPolicyKeys is { Count: <= 32 } && DisabledPolicyKeys.All(Policies.ContainsKey) && Initializers is { Count: <= 32 } &&
        Initializers.All(x => Policies.ContainsKey(x.Key) && ValidKey(x.Value));
    internal static bool ValidKey(string? key) => key is { Length: > 0 and <= 64 } && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
