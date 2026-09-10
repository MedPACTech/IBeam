using IBeam.Identity.Models;

public interface IIdentityAuthService
{
    Task RegisterAsync(RegisterUserRequest request, CancellationToken ct = default);
    Task<AuthResultResponse> PasswordLoginAsync(PasswordLoginRequest request, CancellationToken ct = default);
    Task<RequestPasswordResetResponse> StartEmailPasswordRegistrationAsync(string email, string? displayName = null, string? resetUrlBase = null, CancellationToken ct = default);
    Task<AuthResultResponse> CompleteEmailPasswordRegistrationAsync(string email, string challengeId, string verificationToken, string newPassword, string? displayName = null, CancellationToken ct = default);
    Task<RequestPasswordResetResponse> StartEmailPasswordLinkAsync(Guid userId, string email, string? displayName = null, string? resetUrlBase = null, CancellationToken ct = default);
    Task CompleteEmailPasswordLinkAsync(Guid userId, string email, string challengeId, string verificationToken, string newPassword, CancellationToken ct = default);
    Task<OtpChallengeResult> StartPhoneLinkAsync(Guid userId, string phoneNumber, CancellationToken ct = default);
    Task CompletePhoneLinkAsync(Guid userId, string phoneNumber, string challengeId, string code, CancellationToken ct = default);

    /// <summary>
    /// Adds a verified email to an existing account as an OTP sign-in method, by emailed code.
    /// The email counterpart of <see cref="StartPhoneLinkAsync"/>.
    /// <para>
    /// Distinct from <see cref="StartEmailPasswordLinkAsync"/>, which also sets a password and so
    /// produces an email+password login. OTP sign-in resolves an account purely on the user's email
    /// (see OtpAuthService), so a password is not needed to make an address a working sign-in
    /// method — and requiring one turned "add a second way to sign in" into "choose a password",
    /// which a caller mid-purchase has no reason to do.
    /// </para>
    /// </summary>
    Task<OtpChallengeResult> StartEmailLinkAsync(Guid userId, string email, CancellationToken ct = default);
    Task CompleteEmailLinkAsync(Guid userId, string email, string challengeId, string code, CancellationToken ct = default);
    Task<OtpChallengeResult> StartTwoFactorSetupAsync(Guid userId, string method, CancellationToken ct = default);
    Task CompleteTwoFactorSetupAsync(Guid userId, string method, string challengeId, string code, CancellationToken ct = default);
    Task<AuthResultResponse> CompleteTwoFactorLoginAsync(string email, string challengeId, string code, CancellationToken ct = default);
    Task DisableTwoFactorAsync(Guid userId, CancellationToken ct = default);
    Task SetPreferredTwoFactorMethodAsync(Guid userId, string method, CancellationToken ct = default);
    Task<AuthTokenResponse> SelectTenantAsync(string userId, SelectTenantRequest request, CancellationToken ct = default);
    Task<AuthTokenResponse> SwitchTenantAsync(string userId, SelectTenantRequest request, CancellationToken ct = default);
}
