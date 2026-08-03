using System.Security.Claims;

namespace IBeam.Identity.Models;

public sealed record OAuthAuthorizationRequest(
    string ResponseType,
    string ClientId,
    string RedirectUri,
    string State,
    IReadOnlyList<string> Scopes,
    string Resource,
    string CodeChallenge,
    string CodeChallengeMethod,
    Guid? TenantId = null);

public sealed record OAuthAuthorizationContext(
    string ClientId,
    string ClientDisplayName,
    Guid TenantId,
    Guid UserId,
    string RedirectUri,
    string State,
    IReadOnlyList<string> RequestedScopes,
    string Resource,
    bool ConsentRequired,
    IReadOnlyList<string> PreviouslyGrantedScopes);

public sealed record OAuthAuthorizationDecision(
    OAuthAuthorizationRequest Request,
    bool Approved);

public sealed record OAuthAuthorizationResult(
    string RedirectUri,
    string State,
    string? Code = null,
    string? Error = null,
    string? ErrorDescription = null);

public sealed record OAuthAuthorizationEvaluation(
    OAuthAuthorizationRequest Request,
    OAuthClientRecord Client,
    OAuthConsentRecord? Consent,
    ClaimsPrincipal Subject,
    Guid UserId,
    Guid TenantId);
