using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface IOAuthTokenService
{
    Task<OAuthTokenResponse> ExchangeAsync(OAuthTokenRequest request, CancellationToken ct = default);
    Task RevokeAsync(OAuthRevocationRequest request, CancellationToken ct = default);
}
