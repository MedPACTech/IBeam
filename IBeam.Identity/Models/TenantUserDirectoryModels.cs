namespace IBeam.Identity.Models;

public static class TenantUserDirectoryItemKinds
{
    public const string User = "user";
    public const string Invite = "invite";
}

public static class TenantUserDirectoryStatuses
{
    public const string Active = "active";
    public const string Disabled = "disabled";
    public const string Pending = "pending";
    public const string Sent = "sent";
    public const string Expired = "expired";
    public const string Redeemed = "redeemed";
    public const string Revoked = "revoked";
}

public sealed record TenantUserDirectoryRequest(
    bool IncludePending = false,
    bool IncludeDisabled = false,
    bool PendingOnly = false);

public sealed record TenantUserDirectoryItem(
    string Kind,
    Guid TenantId,
    Guid? UserId = null,
    Guid? InviteId = null,
    string? Email = null,
    string? PhoneNumber = null,
    string? DisplayName = null,
    string? FirstName = null,
    string? LastName = null,
    string Status = TenantUserDirectoryStatuses.Active,
    IReadOnlyList<Guid>? RoleIds = null,
    IReadOnlyList<string>? RoleNames = null,
    DateTimeOffset? CreatedUtc = null,
    DateTimeOffset? UpdatedUtc = null,
    Guid? InvitedByUserId = null,
    DateTimeOffset? ExpiresUtc = null,
    Guid? RedeemedByUserId = null,
    DateTimeOffset? RedeemedUtc = null);

