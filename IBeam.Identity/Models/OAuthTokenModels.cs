namespace IBeam.Identity.Models;

public sealed record OAuthTokenRequest(
    string GrantType,
    string ClientId,
    string? ClientSecret = null,
    string? Code = null,
    string? RedirectUri = null,
    string? CodeVerifier = null,
    string? RefreshToken = null,
    string? Resource = null,
    IReadOnlyList<string>? Scopes = null);

public sealed record OAuthTokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    string Scope,
    string? RefreshToken = null);

public sealed record OAuthRevocationRequest(
    string Token,
    string ClientId,
    string? ClientSecret = null,
    string? TokenTypeHint = null,
    bool RevokeConsent = false,
    string? Resource = null);
