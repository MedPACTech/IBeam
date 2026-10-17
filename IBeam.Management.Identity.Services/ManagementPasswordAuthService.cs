using IBeam.Identity.Models;
using IBeam.Identity.Services.Auth;

namespace IBeam.Management.Identity.Services;

/// <summary>Only password challenge and MFA completion are valid Identity entry points in this realm.</summary>
public sealed class ManagementPasswordAuthService(PasswordAuthService password) : IIdentityAuthService
{
    public Task<AuthResultResponse> PasswordLoginAsync(PasswordLoginRequest request, CancellationToken ct = default)
        => password.PasswordLoginAsync(request, ct);
    public Task<AuthResultResponse> CompleteTwoFactorLoginAsync(string email, string challengeId, string code, CancellationToken ct = default)
        => password.CompleteTwoFactorLoginAsync(email, challengeId, code, ct);

    public Task RegisterAsync(RegisterUserRequest request, CancellationToken ct = default) => throw new ManagementAccessDeniedException();
    public Task<RequestPasswordResetResponse> StartEmailPasswordRegistrationAsync(string email, string? displayName = null, string? resetUrlBase = null, CancellationToken ct = default) => throw new ManagementAccessDeniedException();
    public Task<AuthResultResponse> CompleteEmailPasswordRegistrationAsync(string email, string challengeId, string verificationToken, string newPassword, string? displayName = null, CancellationToken ct = default) => throw new ManagementAccessDeniedException();
    public Task<RequestPasswordResetResponse> StartEmailPasswordLinkAsync(Guid userId, string email, string? displayName = null, string? resetUrlBase = null, CancellationToken ct = default) => throw new ManagementAccessDeniedException();
    public Task CompleteEmailPasswordLinkAsync(Guid userId, string email, string challengeId, string verificationToken, string newPassword, CancellationToken ct = default) => throw new ManagementAccessDeniedException();
    public Task<OtpChallengeResult> StartPhoneLinkAsync(Guid userId, string phoneNumber, CancellationToken ct = default) => throw new ManagementAccessDeniedException();
    public Task CompletePhoneLinkAsync(Guid userId, string phoneNumber, string challengeId, string code, CancellationToken ct = default) => throw new ManagementAccessDeniedException();
    public Task<OtpChallengeResult> StartEmailLinkAsync(Guid userId, string email, CancellationToken ct = default) => throw new ManagementAccessDeniedException();
    public Task CompleteEmailLinkAsync(Guid userId, string email, string challengeId, string code, CancellationToken ct = default) => throw new ManagementAccessDeniedException();
    public Task<OtpChallengeResult> StartTwoFactorSetupAsync(Guid userId, string method, CancellationToken ct = default) => throw new ManagementAccessDeniedException();
    public Task CompleteTwoFactorSetupAsync(Guid userId, string method, string challengeId, string code, CancellationToken ct = default) => throw new ManagementAccessDeniedException();
    public Task DisableTwoFactorAsync(Guid userId, CancellationToken ct = default) => throw new ManagementAccessDeniedException();
    public Task SetPreferredTwoFactorMethodAsync(Guid userId, string method, CancellationToken ct = default) => throw new ManagementAccessDeniedException();
    public Task<AuthTokenResponse> SelectTenantAsync(string userId, SelectTenantRequest request, CancellationToken ct = default) => throw new ManagementAccessDeniedException();
    public Task<AuthTokenResponse> SwitchTenantAsync(string userId, SelectTenantRequest request, CancellationToken ct = default) => throw new ManagementAccessDeniedException();
}
