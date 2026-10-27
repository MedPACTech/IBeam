using IBeam.Management.Services.CustomerIdentity;
namespace IBeam.Management.Identity.Target.Services;

public sealed class ManagementCustomerIdentityTargetOptions
{
    public const string SectionName = "IBeam:Management:CustomerIdentity";
    public Guid IdentityRealmId { get; set; }
    /// <summary>The operator attests this Identity realm belongs only to the configured app/environment.</summary>
    public bool DedicatedApplicationRealm { get; set; }
    /// <summary>Target-owned opt-in ceiling; empty enables discovery only. Changing this configuration requires a host restart.</summary>
    public string[] EnabledOperations { get; set; } = [];
}
