namespace IBeam.Identity.Models;

public sealed record AuthAttemptState(
    int FailedAttempts,
    DateTimeOffset? LockedUntilUtc,
    DateTimeOffset? LastFailedAtUtc,
    DateTimeOffset? LastSucceededAtUtc,
    string? LastFailedIp = null,
    string? LastSucceededIp = null,
    string? LastUserAgent = null,
    string? LastDeviceId = null,
    string? LastCountry = null,
    string? LastRegion = null,
    string? LastCity = null,
    string? LastCorrelationId = null,
    DateTimeOffset? LastUnlockedAtUtc = null,
    string? UnlockedByUserId = null,
    string? UnlockReason = null,
    string? MetadataJson = null)
{
    public bool IsLocked(DateTimeOffset nowUtc)
        => LockedUntilUtc.HasValue && LockedUntilUtc.Value > nowUtc;
}
