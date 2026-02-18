namespace IBeam.Identity.Abstractions.Models;

public enum OtpChannel
{
    Email = 1,
    Sms = 2
}

public sealed record AuthTokenResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt);

public sealed record VerifyOtpResult(
    bool Succeeded,
    string? AccessToken = null,
    DateTimeOffset? ExpiresAt = null,
    string? Error = null);
