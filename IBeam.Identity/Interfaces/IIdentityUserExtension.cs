namespace IBeam.Identity.Interfaces;

public interface IIdentityUserExtension
{
    Guid UserId { get; set; }
    Guid? TenantId { get; set; }
}

public interface IIdentityUserProfileExtension : IIdentityUserExtension
{
    string DisplayName { get; set; }
    string FirstName { get; set; }
    string LastName { get; set; }
}

public interface IIdentityUserContactProjection
{
    string? IdentityEmail { get; set; }
    string? IdentityPhoneNumber { get; set; }
}
