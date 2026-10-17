namespace IBeam.Management.Identity.Services;

public sealed class ManagementIdentityOptions
{
    public const string SectionName = "IBeam:Management:Identity";
    public Guid OrganizationId { get; set; }
    // Trusted one-off setup only; never enable this in a publicly running host.
    public bool BootstrapEnabled { get; set; }
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
