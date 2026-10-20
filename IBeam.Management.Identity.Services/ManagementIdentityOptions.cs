namespace IBeam.Management.Identity.Services;

public sealed class ManagementIdentityOptions
{
    public const string SectionName = "IBeam:Management:Identity";
    public Guid OrganizationId { get; set; }
    // Trusted one-off setup only; never enable this in a publicly running host.
    public bool BootstrapEnabled { get; set; }
    /// <summary>Trusted app-scoped delegation ceilings, never customer operation assignments or implicit admin privileges.</summary>
    public List<ManagementIdentityCustomerDelegation> CustomerDelegations { get; set; } = [];
}
public sealed class ManagementIdentityCustomerDelegation
{
    public Guid UserId { get; set; }
    public Guid ApplicationId { get; set; }
    public string[] Operations { get; set; } = [];
}

public static class ManagementIdentityRoles
{
    public const string Administrator = "ManagementAdministrator";
    public const string Support = "ManagementSupport";
}

public enum ManagementIdentityStorageMode { Separate, Shared }

public sealed class ManagementIdentityStorageOptions
{
    public ManagementIdentityStorageMode Mode { get; set; }
    public string ConnectionString { get; set; } = string.Empty;
    public string TablePrefix { get; set; } = "Management";
    public bool ProvisionSchema { get; set; }
    public override string ToString() => "Management identity storage (credentials redacted)";
}
