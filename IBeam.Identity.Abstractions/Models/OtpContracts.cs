namespace IBeam.Identity.Abstractions.Models;

//public enum OtpPurpose
//{
//    Login = 1,
//    PasswordReset = 2,
//    Register = 3,
//    ChangeEmail = 4,
//    ChangePhone = 5
//}

//public enum OtpChannel
//{
//    Email = 1,
//    Sms = 2
//}

public sealed record CreateOtpChallengeRequest(
    OtpPurpose Purpose,
    OtpChannel Channel,
    string To,
    string? TenantHint = null
);

public sealed record CreateOtpChallengeResponse(
    Guid ChallengeId,
    DateTimeOffset ExpiresAt,
    string MaskedDestination
);

public sealed record VerifyOtpChallengeRequest(
    Guid ChallengeId,
    string Code
);

public sealed record VerifyOtpChallengeResponse(
    bool Verified,
    string? VerificationToken,     // one-time token to continue reset/registration/etc.
    DateTimeOffset? ExpiresAt
);

public sealed record ResendOtpChallengeResponse(
    Guid ChallengeId,
    DateTimeOffset ExpiresAt,
    string MaskedDestination
);
