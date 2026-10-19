namespace IBeam.Management.Identity.Target.Services.Delegation;

public sealed class ManagementActorTrustOptions
{
    public const string SectionName = "IBeam:Management:CustomerIdentity:Delegation";
    public List<ManagementActorTrustBinding> TrustedPortals { get; set; } = [];
}
public sealed class ManagementActorTrustBinding
{
    public Guid OrganizationId { get; set; }
    public Guid ApplicationId { get; set; }
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
}
