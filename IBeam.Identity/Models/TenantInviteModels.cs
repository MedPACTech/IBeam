namespace IBeam.Identity.Models;

public static class TenantInviteDestinationTypes
{
    public const string Email = "email";
    public const string Sms = "sms";
}

public static class TenantInviteStatuses
{
    public const string Pending = "pending";
    public const string Sent = "sent";
    public const string Redeemed = "redeemed";
    public const string Revoked = "revoked";
    public const string Expired = "expired";
}

public static class TenantInviteAcceptModes
{
    public const string Otp = "otp";
    public const string EmailPassword = "email-password";
    public const string SmsOtp = "sms-otp";
    public const string ExistingSession = "existing-session";
}

public sealed record TenantInviteProfileHints(
    string? DisplayName = null,
    string? FirstName = null,
    string? LastName = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record TenantInviteAccessGrantRequest(
    string ResourceType,
    string ResourceId,
    string AccessLevel,
    DateTimeOffset? ExpirationUtc = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record TenantInviteCreateRequest(
    string DestinationType,
    string? Email = null,
    string? PhoneNumber = null,
    string? DisplayName = null,
    string? FirstName = null,
    string? LastName = null,
    IReadOnlyList<Guid>? RoleIds = null,
    IReadOnlyList<string>? RoleNames = null,
    bool SetAsDefaultTenant = false,
    IReadOnlyList<TenantInviteAccessGrantRequest>? AccessGrants = null,
    DateTimeOffset? ExpiresUtc = null,
    string? RedirectUrl = null,
    IReadOnlyDictionary<string, string>? Metadata = null,
    string? CorrelationId = null,
    string? CausationId = null);

public sealed record TenantInviteAcceptRequest(
    string? InviteToken = null,
    string? InviteCode = null,
    string? Mode = null,
    string? ChallengeId = null,
    string? Code = null,
    string? VerificationToken = null,
    string? Password = null,
    string? DisplayName = null,
    string? FirstName = null,
    string? LastName = null,
    bool? SetAsDefaultTenant = null,
    string? CorrelationId = null,
    string? CausationId = null);

public sealed record TenantInviteRecord(
    Guid InviteId,
    Guid TenantId,
    string DestinationType,
    string NormalizedDestination,
    string TokenHash,
    string Status,
    DateTimeOffset CreatedUtc,
    Guid InvitedByUserId,
    DateTimeOffset ExpiresUtc,
    DateTimeOffset? SentUtc = null,
    DateTimeOffset? RedeemedUtc = null,
    Guid? RedeemedByUserId = null,
    DateTimeOffset? RevokedUtc = null,
    Guid? RevokedByUserId = null,
    string? RevokedReason = null,
    TenantInviteProfileHints? ProfileHints = null,
    IReadOnlyList<Guid>? RoleIds = null,
    IReadOnlyList<string>? RoleNames = null,
    bool SetAsDefaultTenant = false,
    IReadOnlyList<TenantInviteAccessGrantRequest>? AccessGrants = null,
    string? RedirectUrl = null,
    string? CorrelationId = null,
    string? CausationId = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public bool IsRedeemable(DateTimeOffset now)
        => (string.Equals(Status, TenantInviteStatuses.Pending, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Status, TenantInviteStatuses.Sent, StringComparison.OrdinalIgnoreCase)) &&
           ExpiresUtc > now;
}

public sealed record TenantInviteInfo(
    Guid InviteId,
    Guid TenantId,
    string DestinationType,
    string NormalizedDestination,
    string Status,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ExpiresUtc,
    Guid InvitedByUserId,
    DateTimeOffset? SentUtc,
    DateTimeOffset? RedeemedUtc,
    Guid? RedeemedByUserId,
    DateTimeOffset? RevokedUtc,
    Guid? RevokedByUserId,
    string? RevokedReason,
    TenantInviteProfileHints? ProfileHints,
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<string> RoleNames,
    bool SetAsDefaultTenant,
    IReadOnlyList<TenantInviteAccessGrantRequest> AccessGrants,
    string? RedirectUrl,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static TenantInviteInfo FromRecord(TenantInviteRecord record)
        => new(
            record.InviteId,
            record.TenantId,
            record.DestinationType,
            record.NormalizedDestination,
            record.Status,
            record.CreatedUtc,
            record.ExpiresUtc,
            record.InvitedByUserId,
            record.SentUtc,
            record.RedeemedUtc,
            record.RedeemedByUserId,
            record.RevokedUtc,
            record.RevokedByUserId,
            record.RevokedReason,
            record.ProfileHints,
            record.RoleIds?.Where(x => x != Guid.Empty).Distinct().ToList() ?? [],
            record.RoleNames?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [],
            record.SetAsDefaultTenant,
            record.AccessGrants?.ToList() ?? [],
            record.RedirectUrl,
            record.Metadata?.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase) ?? []);
}

public sealed record TenantInviteCreatedResult(
    TenantInviteInfo Invite,
    string InviteToken,
    string InviteUrl);

public sealed record TenantInvitePreview(
    Guid InviteId,
    Guid TenantId,
    string DestinationType,
    string NormalizedDestination,
    DateTimeOffset ExpiresUtc,
    string Status,
    TenantInviteProfileHints? ProfileHints,
    string? RedirectUrl);

public sealed record TenantInviteAcceptResult(
    TenantInviteInfo Invite,
    IdentityUser User,
    TenantInfo Membership,
    TokenResult? Token,
    bool CreatedNewUser,
    IReadOnlyList<TenantRole> Roles);
