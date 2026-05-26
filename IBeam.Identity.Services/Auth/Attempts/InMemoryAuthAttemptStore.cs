using System.Collections.Concurrent;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;

namespace IBeam.Identity.Services.Auth.Attempts;

public sealed class InMemoryAuthAttemptStore : IAuthAttemptStore
{
    private readonly ConcurrentDictionary<string, AuthAttemptState> _states = new(StringComparer.Ordinal);

    public Task<AuthAttemptState> GetStateAsync(string method, string identifier, CancellationToken ct = default)
    {
        var key = Key(method, identifier);
        return Task.FromResult(_states.TryGetValue(key, out var state)
            ? state
            : new AuthAttemptState(0, null, null, null));
    }

    public Task<AuthAttemptState> RegisterFailureAsync(string method, string identifier, int maxFailedAttempts, TimeSpan lockoutDuration, CancellationToken ct = default)
    {
        var key = Key(method, identifier);
        var now = DateTimeOffset.UtcNow;

        var updated = _states.AddOrUpdate(
            key,
            _ => BuildFailedState(0, now, maxFailedAttempts, lockoutDuration),
            (_, current) => BuildFailedState(current.FailedAttempts, now, maxFailedAttempts, lockoutDuration));

        return Task.FromResult(updated);
    }

    public Task<AuthAttemptState> RegisterSuccessAsync(string method, string identifier, CancellationToken ct = default)
    {
        var key = Key(method, identifier);
        var now = DateTimeOffset.UtcNow;
        var updated = new AuthAttemptState(0, null, null, now);
        _states.AddOrUpdate(key, _ => updated, (_, __) => updated);
        return Task.FromResult(updated);
    }

    private static AuthAttemptState BuildFailedState(int existingFailedCount, DateTimeOffset now, int maxFailedAttempts, TimeSpan lockoutDuration)
    {
        var failed = existingFailedCount + 1;
        DateTimeOffset? lockedUntil = failed >= maxFailedAttempts ? now.Add(lockoutDuration) : null;
        return new AuthAttemptState(failed, lockedUntil, now, null);
    }

    private static string Key(string method, string identifier)
        => $"{(method ?? string.Empty).Trim().ToLowerInvariant()}|{(identifier ?? string.Empty).Trim().ToLowerInvariant()}";
}
