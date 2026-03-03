namespace IBeam.Identity.Interfaces;

using IBeam.Identity.Models;

public interface ITokenService
{
    Task<TokenResult> CreateAccessTokenAsync(
        Guid userId,
        Guid tenantId,
        IReadOnlyList<ClaimItem> claims,
        CancellationToken ct = default);

    Task<TokenResult> RefreshAccessTokenAsync(
        string refreshToken,
        CancellationToken ct = default);

    Task<IReadOnlyList<AuthSessionInfo>> GetUserSessionsAsync(
        Guid userId,
        CancellationToken ct = default);

    Task<bool> RevokeSessionAsync(
        Guid userId,
        string sessionId,
        CancellationToken ct = default);

    Task<TokenResult> CreatePreTenantTokenAsync(
        Guid userId,
        IReadOnlyList<ClaimItem> claims,
        CancellationToken ct = default);
}
