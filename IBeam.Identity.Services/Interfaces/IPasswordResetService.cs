using IBeam.Identity.Core.PasswordReset.Contracts;

namespace IBeam.Identity.Core.PasswordReset.Interfaces;

public interface IPasswordResetService
{
    /// <summary>
    /// Starts a password reset workflow for an identifier (email/username/phone).
    /// MUST be enumeration-safe (always succeeds outwardly).
    /// </summary>
    Task<RequestPasswordResetResponse> RequestAsync(RequestPasswordResetRequest req, CancellationToken ct);

    /// <summary>
    /// Confirms the reset using a one-time verification token (typically issued by OTP verify),
    /// and sets the new password.
    /// </summary>
    Task ConfirmAsync(ConfirmPasswordResetRequest req, CancellationToken ct);

    /// <summary>
    /// Optional: lets the UI check if a verification token is still valid before submitting.
    /// </summary>
    Task<ValidatePasswordResetTokenResponse> ValidateTokenAsync(ValidatePasswordResetTokenRequest req, CancellationToken ct);
}
