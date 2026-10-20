using IBeam.Management.Identity.Target.Services.Provisioning;
namespace IBeam.Management.Identity.Target.Services.Invitations;

public sealed class ManagementCustomerInvitationOptions
{
    public const string SectionName = "IBeam:Management:CustomerIdentity:Invitations";
    public string[] ApprovedRoleNames { get; set; } = [];
    public int MinimumResendSeconds { get; set; } = 60;
    public int MaximumResendsPerDay { get; set; } = 5;
    public bool IsValid() => ApprovedRoleNames.Length is > 0 and <= 32 && ApprovedRoleNames.All(ManagementCustomerProvisioningOptions.ValidRoleName) &&
        ApprovedRoleNames.Distinct(StringComparer.Ordinal).Count() == ApprovedRoleNames.Length && MinimumResendSeconds is >= 60 and <= 86400 && MaximumResendsPerDay is >= 1 and <= 20;
}
