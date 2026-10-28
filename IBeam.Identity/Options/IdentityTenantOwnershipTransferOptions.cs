using IBeam.Identity.Models;
namespace IBeam.Identity.Options;

public sealed class IdentityTenantOwnershipTransferOptions
{
    public const string SectionName = "IBeam:Identity:TenantOwnershipTransfer";
    public bool Enabled { get; set; }
    public IdentityTenantOwnerMode Mode { get; set; }
    public int MaximumMembers { get; set; } = 5000;
    public bool IsValid() => MaximumMembers is > 0 and <= 5000 &&
        (!Enabled || Mode is IdentityTenantOwnerMode.SingleOwner or IdentityTenantOwnerMode.MultipleOwners);
}
