namespace IBeam.Identity.Options;

public sealed class LoginAttemptOptions
{
    public const string SectionName = "IBeam:Identity:LoginAttempts";

    public bool Enabled { get; init; } = true;
    public int MaxFailedAttempts { get; init; } = 5;
    public int LockoutMinutes { get; init; } = 10;

    public void Validate()
    {
        if (MaxFailedAttempts < 1)
            throw new InvalidOperationException("LoginAttempts:MaxFailedAttempts must be >= 1.");
        if (LockoutMinutes < 1)
            throw new InvalidOperationException("LoginAttempts:LockoutMinutes must be >= 1.");
    }
}
