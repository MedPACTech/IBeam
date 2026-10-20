using IBeam.Management.Identity.Target.Services.Provisioning;
namespace IBeam.Management.Identity.Target.Services.Memberships;
public sealed class ManagementCustomerMembershipOptions
{
    public const string SectionName = "IBeam:Management:CustomerIdentity:Memberships";
    public string[] AdministratorRoleNames { get; set; } = [];
    public bool IsValid() => AdministratorRoleNames is { Length: > 0 and <= 8 } && AdministratorRoleNames.All(x => ManagementCustomerProvisioningOptions.ValidRoleName(x) && !x.Contains(',')) &&
        AdministratorRoleNames.Distinct(StringComparer.Ordinal).Count() == AdministratorRoleNames.Length;
}
