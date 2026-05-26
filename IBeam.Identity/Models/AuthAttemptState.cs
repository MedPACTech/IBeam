namespace IBeam.Identity.Models;

public sealed record AuthAttemptState(
    int FailedAttempts,
    DateTimeOffset? LockedUntilUtc,
    DateTimeOffset? LastFailedAtUtc,
    DateTimeOffset? LastSucceededAtUtc)
{
    public bool IsLocked(DateTimeOffset nowUtc)
        => LockedUntilUtc.HasValue && LockedUntilUtc.Value > nowUtc;
}
