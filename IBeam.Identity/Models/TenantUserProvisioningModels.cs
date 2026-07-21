namespace IBeam.Identity.Models;

public sealed record ProvisionTenantUserRequest(
    string? Email = null,
    string? PhoneNumber = null,
    string? DisplayName = null,
    string? FirstName = null,
    string? LastName = null,
    IReadOnlyList<Guid>? RoleIds = null,
    IReadOnlyList<string>? RoleNames = null,
    IReadOnlyList<TenantInviteAccessGrantRequest>? AccessGrants = null,
    bool SetAsDefaultTenant = false,
    bool SendInvite = false,
    bool RequirePasswordSetup = false,
    string? RedirectUrl = null,
    string? CorrelationId = null,
    string? CausationId = null,
    IReadOnlyDictionary<string, string>? ProfileMetadata = null);

public sealed record ProvisionTenantUserResult(
    IdentityUser User,
    TenantInfo Membership,
    IReadOnlyList<TenantRole> Roles,
    bool CreatedNewUser,
    TenantInviteInfo? Invite = null);

