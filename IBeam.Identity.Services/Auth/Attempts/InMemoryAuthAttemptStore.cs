using System.Collections.Concurrent;
using System.Text.Json;
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

    public Task<AuthAttemptState> RegisterFailureAsync(
        string method,
        string identifier,
        int maxFailedAttempts,
        TimeSpan lockoutDuration,
        CancellationToken ct = default,
        AuthAttemptContext? context = null)
    {
        var key = Key(method, identifier);
        var now = DateTimeOffset.UtcNow;

        var updated = _states.AddOrUpdate(
            key,
            _ => BuildFailedState(0, now, maxFailedAttempts, lockoutDuration, context),
            (_, current) => BuildFailedState(current.FailedAttempts, now, maxFailedAttempts, lockoutDuration, context));

        return Task.FromResult(updated);
    }

    public Task<AuthAttemptState> RegisterSuccessAsync(
        string method,
        string identifier,
        CancellationToken ct = default,
        AuthAttemptContext? context = null)
    {
        var key = Key(method, identifier);
        var now = DateTimeOffset.UtcNow;
        var updated = new AuthAttemptState(
            0,
            null,
            null,
            now,
            LastSucceededIp: context?.IpAddress,
            LastUserAgent: context?.UserAgent,
            LastDeviceId: context?.DeviceId,
            LastCountry: context?.Country,
            LastRegion: context?.Region,
            LastCity: context?.City,
            LastCorrelationId: context?.CorrelationId,
            MetadataJson: SerializeMetadata(context));
        _states.AddOrUpdate(key, _ => updated, (_, __) => updated);
        return Task.FromResult(updated);
    }

    public Task<AuthAttemptState> UnlockAsync(
        string method,
        string identifier,
        Guid? unlockedByUserId = null,
        string? reason = null,
        CancellationToken ct = default,
        AuthAttemptContext? context = null)
    {
        var key = Key(method, identifier);
        var now = DateTimeOffset.UtcNow;
        var updated = new AuthAttemptState(
            0,
            null,
            null,
            null,
            LastUserAgent: context?.UserAgent,
            LastDeviceId: context?.DeviceId,
            LastCountry: context?.Country,
            LastRegion: context?.Region,
            LastCity: context?.City,
            LastCorrelationId: context?.CorrelationId,
            LastUnlockedAtUtc: now,
            UnlockedByUserId: unlockedByUserId?.ToString("D"),
            UnlockReason: string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            MetadataJson: SerializeMetadata(context));
        _states.AddOrUpdate(key, _ => updated, (_, __) => updated);
        return Task.FromResult(updated);
    }

    public Task ClearAsync(string method, string identifier, CancellationToken ct = default)
    {
        _states.TryRemove(Key(method, identifier), out _);
        return Task.CompletedTask;
    }

    private static AuthAttemptState BuildFailedState(
        int existingFailedCount,
        DateTimeOffset now,
        int maxFailedAttempts,
        TimeSpan lockoutDuration,
        AuthAttemptContext? context)
    {
        var failed = existingFailedCount + 1;
        DateTimeOffset? lockedUntil = failed >= maxFailedAttempts ? now.Add(lockoutDuration) : null;
        return new AuthAttemptState(
            failed,
            lockedUntil,
            now,
            null,
            LastFailedIp: context?.IpAddress,
            LastUserAgent: context?.UserAgent,
            LastDeviceId: context?.DeviceId,
            LastCountry: context?.Country,
            LastRegion: context?.Region,
            LastCity: context?.City,
            LastCorrelationId: context?.CorrelationId,
            MetadataJson: SerializeMetadata(context));
    }

    private static string Key(string method, string identifier)
        => $"{(method ?? string.Empty).Trim().ToLowerInvariant()}|{(identifier ?? string.Empty).Trim().ToLowerInvariant()}";

    private static string? SerializeMetadata(AuthAttemptContext? context)
        => context?.Metadata is { Count: > 0 }
            ? JsonSerializer.Serialize(context.Metadata)
            : null;
}
