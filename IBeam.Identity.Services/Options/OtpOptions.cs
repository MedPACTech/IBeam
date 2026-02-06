namespace IBeam.Identity.Services.Otp.Options;

public sealed class OtpOptions
{
    public int CodeLength { get; set; } = 6;
    public TimeSpan CodeTtl { get; set; } = TimeSpan.FromMinutes(10);
    public int MaxVerifyAttempts { get; set; } = 5;
    public TimeSpan ResendCooldown { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan VerificationTokenTtl { get; set; } = TimeSpan.FromMinutes(10);

    // Should be set via configuration; do not ship with CHANGE_ME in production
    public string HashSalt { get; set; } = "CHANGE_ME";
}
