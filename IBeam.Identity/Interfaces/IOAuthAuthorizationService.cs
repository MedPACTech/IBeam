using System.Security.Claims;
using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface IOAuthAuthorizationService
{
    Task<OAuthAuthorizationContext> PrepareAsync(ClaimsPrincipal subject, OAuthAuthorizationRequest request, CancellationToken ct = default);
    Task<OAuthAuthorizationResult> AuthorizeAsync(ClaimsPrincipal subject, OAuthAuthorizationDecision decision, CancellationToken ct = default);
}
