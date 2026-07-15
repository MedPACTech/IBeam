namespace IBeam.Identity.Options;

public sealed class OtpOptions
{
    public const string SectionName = "IBeam:Identity:Otp";

    public int CodeLength { get; init; } = 6;
    public int ExpirationMinutes { get; init; } = 5;
    public int MaxAttempts { get; init; } = 5;
    public int LockoutMinutes { get; init; } = 10;
    public int MaxChallengeRequests { get; init; } = 5;
    public int ChallengeRequestLockoutMinutes { get; init; } = 15;
    public int MaxFailedAttemptsPerIp { get; init; } = 20;
    public int IpLockoutMinutes { get; init; } = 30;
    public int FailureResponseDelayMilliseconds { get; init; } = 250;
    public bool TrackAttemptMetadata { get; init; } = true;

    // NEW: how long the verification token stays valid after successful OTP verification
    public int VerificationTokenMinutes { get; init; } = 10;

    // NEW: used when hashing codes at rest
    // You can also move this to a secure secret provider later (KeyVault, etc.)
    public string HashSalt { get; init; } = "change-me";

    public string VerificationTokenSecret { get; set; } = "";

    public bool AllowAutoProvisionForUnknownUser { get; set; }

    public void Validate()
    {
        if (CodeLength < 1)
            throw new InvalidOperationException("Otp:CodeLength must be >= 1.");
        if (ExpirationMinutes < 1)
            throw new InvalidOperationException("Otp:ExpirationMinutes must be >= 1.");
        if (MaxAttempts < 0)
            throw new InvalidOperationException("Otp:MaxAttempts must be >= 0. Use 0 to disable OTP attempt lockout.");
        if (LockoutMinutes < 1)
            throw new InvalidOperationException("Otp:LockoutMinutes must be >= 1.");
        if (MaxChallengeRequests < 0)
            throw new InvalidOperationException("Otp:MaxChallengeRequests must be >= 0. Use 0 to disable OTP challenge request throttling.");
        if (ChallengeRequestLockoutMinutes < 1)
            throw new InvalidOperationException("Otp:ChallengeRequestLockoutMinutes must be >= 1.");
        if (MaxFailedAttemptsPerIp < 0)
            throw new InvalidOperationException("Otp:MaxFailedAttemptsPerIp must be >= 0. Use 0 to disable IP-based OTP lockout.");
        if (IpLockoutMinutes < 1)
            throw new InvalidOperationException("Otp:IpLockoutMinutes must be >= 1.");
        if (FailureResponseDelayMilliseconds < 0)
            throw new InvalidOperationException("Otp:FailureResponseDelayMilliseconds must be >= 0.");
        if (VerificationTokenMinutes < 1)
            throw new InvalidOperationException("Otp:VerificationTokenMinutes must be >= 1.");
    }
}

