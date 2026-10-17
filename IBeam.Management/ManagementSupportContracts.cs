using System.Text.Json.Serialization;
namespace IBeam.Management;

public static class ManagementSupportOperations
{
    public const string UsersAdmin = "management.support-users.admin";
    public const string AccessAdmin = "management.application-access.admin";
}
public sealed record ManagementSupportUserDto(Guid UserId, string? Email, bool IsEnabled, bool IsAdministrator);
public sealed record ManagementSupportUserState(Guid OrganizationId, Guid UserId, bool IsEnabled, bool IsAdministrator);
public interface IManagementSupportUserStateProvider
{
    Task<ManagementSupportUserState?> GetAsync(Guid organizationId, Guid userId, CancellationToken ct = default);
}
public sealed class ManagementSupportInvitationRequest
{
    public string Email { get; set; } = "";
}
public sealed class ManagementSupportInvitationAcceptRequest
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)] public string InviteToken { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)] public string Password { get; set; } = "";
    public override string ToString() => "Support invitation acceptance (redacted)";
}
public sealed record ManagementSupportInvitationDto(Guid InviteId, DateTimeOffset ExpiresUtc, string Status);
public sealed record ManagementSupportInvitationAccepted(Guid UserId, bool RequiresMfaLogin = true);
public interface IManagementSupportUserService
{
    Task<IReadOnlyList<ManagementSupportUserDto>> ListAsync(CancellationToken ct = default);
    Task<ManagementSupportInvitationDto> InviteAsync(ManagementSupportInvitationRequest request, CancellationToken ct = default);
    Task<ManagementSupportInvitationAccepted> AcceptAsync(ManagementSupportInvitationAcceptRequest request, CancellationToken ct = default);
    Task DisableAsync(Guid userId, CancellationToken ct = default);
    Task UpdateAsync(Guid userId, bool isEnabled, CancellationToken ct = default);
}
/// <summary>Atomic one-time claim in the existing management Identity TenantInvites table; contains no token or profile.</summary>
public interface IManagementInvitationRedemptionStore
{
    Task<bool> TryClaimAsync(Guid organizationId, Guid inviteId, Guid userId, CancellationToken ct = default);
}
public sealed class ManagementSupportInvalidException() : Exception("The support request or invitation is invalid.");
public sealed class ManagementSupportUnavailableException() : Exception("Support administration is temporarily unavailable.");
