namespace IBeam.Identity.Models;

public sealed record RequestPasswordResetRequest(
    string Identifier,        // email/username/phone
    string? TenantHint = null
);

public sealed record RequestPasswordResetResponse(
    bool Accepted,            // always true outwardly (anti-enumeration)
    string? ChallengeId = null
);

public sealed record ConfirmPasswordResetRequest(
    string VerificationToken, // from OTP verify
    string NewPassword
);

public sealed record ValidatePasswordResetTokenRequest(
    string VerificationToken
);

public sealed record ValidatePasswordResetTokenResponse(
    bool Valid,
    DateTimeOffset? ExpiresAt
);
