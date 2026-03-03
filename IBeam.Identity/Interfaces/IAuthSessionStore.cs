using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface IAuthSessionStore
{
    Task SaveAsync(AuthSessionRecord record, CancellationToken ct = default);
    Task<AuthSessionRecord?> GetByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken ct = default);
    Task DeleteByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken ct = default);
    Task<IReadOnlyList<AuthSessionRecord>> GetByUserAsync(Guid userId, CancellationToken ct = default);
    Task<bool> RevokeBySessionIdAsync(Guid userId, string sessionId, CancellationToken ct = default);
}
