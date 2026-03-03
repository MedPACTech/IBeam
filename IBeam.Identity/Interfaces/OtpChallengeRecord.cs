namespace IBeam.Identity.Models;


//TODO: We need a clean seperation of Models and Entities.
//This record is used for both the database entity and the model returned by the service.
//We should split it into two separate types, one for the database and one for the service layer, to maintain a clear separation of concerns.
public sealed record OtpChallengeRecord(
    string ChallengeId,
    string Destination, // email address or phone number
    SenderPurpose Purpose,
    string CodeHash,
    DateTimeOffset ExpiresAt,
    int AttemptCount,
    Guid? TenantId,
    bool IsConsumed,
    string? VerificationToken,
    DateTimeOffset? VerificationTokenExpiresAt);

