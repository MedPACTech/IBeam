namespace IBeam.Identity.Abstractions.Options;

public sealed class OtpOptions
{
    public int CodeLength { get; init; } = 6;
    public int ExpirationMinutes { get; init; } = 5;
    public int MaxAttempts { get; init; } = 5;

    // NEW: how long the verification token stays valid after successful OTP verification
    public int VerificationTokenMinutes { get; init; } = 10;

    // NEW: used when hashing codes at rest
    // You can also move this to a secure secret provider later (KeyVault, etc.)
    public string HashSalt { get; init; } = "change-me";
}

