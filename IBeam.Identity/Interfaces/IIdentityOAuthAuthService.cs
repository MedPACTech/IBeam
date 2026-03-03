using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface IIdentityOAuthAuthService
{
    Task<OAuthStartResponse> StartOAuthAsync(string provider, string redirectUri, CancellationToken ct = default);
    Task<AuthResultResponse> CompleteOAuthAsync(OAuthCallbackRequest request, CancellationToken ct = default);
    Task<OAuthStartResponse> StartOAuthLinkAsync(Guid userId, string provider, string redirectUri, CancellationToken ct = default);
    Task LinkOAuthAsync(Guid userId, OAuthCallbackRequest request, CancellationToken ct = default);
    Task UnlinkOAuthAsync(Guid userId, string provider, CancellationToken ct = default);
    Task<IReadOnlyList<LinkedOAuthProvider>> GetLinkedProvidersAsync(Guid userId, CancellationToken ct = default);
}
