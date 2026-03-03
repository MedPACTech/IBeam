namespace IBeam.Identity.Models;

public sealed record OAuthStartResponse(
    string Provider,
    string AuthorizationUrl,
    string State,
    DateTimeOffset ExpiresAt);

public sealed record OAuthCallbackRequest(
    string Provider,
    string State,
    string Code,
    string RedirectUri);
