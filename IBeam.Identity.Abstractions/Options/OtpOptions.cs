namespace IBeam.Identity.Abstractions.Options;

public sealed class OtpOptions
{
    public int CodeLength { get; init; } = 6;
    public int ExpirationMinutes { get; init; } = 5;
    public int MaxAttempts { get; init; } = 5;
}
