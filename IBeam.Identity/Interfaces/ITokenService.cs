namespace IBeam.Identity.Interfaces;

using IBeam.Identity.Models;

public interface ITokenService
{
    Task<TokenResult> CreateAccessTokenAsync(
        Guid userId,
        Guid tenantId,
        IReadOnlyList<ClaimItem> claims,
        CancellationToken ct = default);

    /// <summary>
    /// Same as the 4-argument overload, but <paramref name="rememberDevice"/> lets this specific
    /// login opt into a longer-lived session (JwtOptions.RememberedRefreshTokenDays /
    /// RememberedSessionAbsoluteLifetimeDays) than JwtOptions' normal RefreshTokenDays /
    /// SessionAbsoluteLifetimeDays would otherwise grant. The refresh token issued here carries
    /// that choice through every future rotation (see RefreshAccessTokenAsync) without the caller
    /// needing to re-assert it.
    /// </summary>
    Task<TokenResult> CreateAccessTokenAsync(
        Guid userId,
        Guid tenantId,
        IReadOnlyList<ClaimItem> claims,
        bool rememberDevice,
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
