using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface IAuthAttemptStore
{
    Task<AuthAttemptState> GetStateAsync(string method, string identifier, CancellationToken ct = default);
    Task<AuthAttemptState> RegisterFailureAsync(
        string method,
        string identifier,
        int maxFailedAttempts,
        TimeSpan lockoutDuration,
        CancellationToken ct = default,
        AuthAttemptContext? context = null);

    Task<AuthAttemptState> RegisterSuccessAsync(
        string method,
        string identifier,
        CancellationToken ct = default,
        AuthAttemptContext? context = null);

    Task<AuthAttemptState> UnlockAsync(
        string method,
        string identifier,
        Guid? unlockedByUserId = null,
        string? reason = null,
        CancellationToken ct = default,
        AuthAttemptContext? context = null);

    Task ClearAsync(string method, string identifier, CancellationToken ct = default);
}
