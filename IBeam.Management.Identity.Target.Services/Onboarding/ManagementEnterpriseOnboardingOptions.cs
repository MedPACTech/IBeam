namespace IBeam.Management.Identity.Target.Services.Onboarding;

public sealed class ManagementEnterpriseOnboardingOptions
{
    public const string SectionName = "IBeam:Management:CustomerIdentity:Onboarding";
    /// <summary>Host-owned keys mapped to an existing target-local Admin or Owner role seeded by lifecycle hooks.</summary>
    public Dictionary<string, string> Policies { get; set; } = new(StringComparer.Ordinal);
    public bool IsValid() => Policies is { Count: > 0 and <= 32 } && Policies.All(x =>
        x.Key.Length is > 0 and <= 64 && x.Key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') &&
        x.Value is "Admin" or "Owner");
}
