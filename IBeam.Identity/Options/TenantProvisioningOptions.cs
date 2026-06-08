namespace IBeam.Identity.Options;

public sealed class TenantProvisioningOptions
{
    public const string SectionName = "IBeam:Identity:TenantProvisioning";

    public TenantProvisioningMode Mode { get; set; } = TenantProvisioningMode.AutoCreateTenantForNewUser;
    public Guid? DefaultTenantId { get; set; }
    public bool AutoLinkUserToDefaultTenant { get; set; }
    public List<string> AutoLinkRoleNames { get; set; } = new();

    public void Validate()
    {
        if (Mode == TenantProvisioningMode.UseDefaultTenant && (!DefaultTenantId.HasValue || DefaultTenantId.Value == Guid.Empty))
            throw new InvalidOperationException($"{SectionName}:DefaultTenantId is required when Mode is UseDefaultTenant.");
    }
}

public enum TenantProvisioningMode
{
    AutoCreateTenantForNewUser = 0,
    RequireExistingTenant = 1,
    UseDefaultTenant = 2
}
