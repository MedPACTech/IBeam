namespace IBeam.Management.Diagnostics.Api;

public sealed class ManagementDiagnosticsApiOptions
{
    public const string SectionName = "IBeam:Management:Diagnostics:Api";
    /// <summary>A host-registered scheme validating only this management trust domain.</summary>
    public string AuthenticationScheme { get; set; } = string.Empty;
}

public static class ManagementDiagnosticsApiPolicies
{
    public const string Access = "IBeam.Management.TargetCredential";
}
