using IBeam.Identity.Abstractions.Models;

public interface IIdentityAuthService
{
    Task RegisterAsync(RegisterUserRequest request, CancellationToken ct = default);
    Task<AuthResultResponse> PasswordLoginAsync(PasswordLoginRequest request, CancellationToken ct = default);
    Task<RequestPasswordResetResponse> StartEmailPasswordRegistrationAsync(string email, string? displayName = null, string? resetUrlBase = null, CancellationToken ct = default);
    Task<AuthResultResponse> CompleteEmailPasswordRegistrationAsync(string email, string challengeId, string verificationToken, string newPassword, string? displayName = null, CancellationToken ct = default);
    Task<OtpChallengeResult> StartTwoFactorSetupAsync(Guid userId, string method, CancellationToken ct = default);
    Task CompleteTwoFactorSetupAsync(Guid userId, string method, string challengeId, string code, CancellationToken ct = default);
    Task<AuthResultResponse> CompleteTwoFactorLoginAsync(string email, string challengeId, string code, CancellationToken ct = default);
    Task DisableTwoFactorAsync(Guid userId, CancellationToken ct = default);
    Task SetPreferredTwoFactorMethodAsync(Guid userId, string method, CancellationToken ct = default);
    Task<AuthTokenResponse> SelectTenantAsync(string userId, SelectTenantRequest request, CancellationToken ct = default);
    Task<AuthTokenResponse> SwitchTenantAsync(string userId, SelectTenantRequest request, CancellationToken ct = default);
}
