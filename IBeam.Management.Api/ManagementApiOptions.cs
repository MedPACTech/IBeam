namespace IBeam.Management.Api;

public sealed class ManagementApiOptions
{
    public const string SectionName = "IBeam:Management:Api";
    /// <summary>A host-registered scheme validating only this management trust domain.</summary>
    public string AuthenticationScheme { get; set; } = string.Empty;
}

public static class ManagementApiPolicies
{
    public const string Access = "IBeam.Management.SupportUser";
}
