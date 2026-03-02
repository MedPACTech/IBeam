using IBeam.Identity.Abstractions.Models;

namespace IBeam.Identity.Abstractions.Interfaces;

public interface IIdentityOAuthAuthService
{
    Task<OAuthStartResponse> StartOAuthAsync(string provider, string redirectUri, CancellationToken ct = default);
    Task<AuthResultResponse> CompleteOAuthAsync(OAuthCallbackRequest request, CancellationToken ct = default);
}
