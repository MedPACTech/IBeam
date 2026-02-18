namespace IBeam.Identity.Abstractions.Interfaces;

using IBeam.Identity.Abstractions.Models;

public interface ITokenService
{
    Task<TokenResult> CreateAccessTokenAsync(Guid userId, Guid tenantId, IReadOnlyList<ClaimItem> claims, CancellationToken ct = default);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
