using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Services.Auth;

public sealed class OAuthAuthorizationService : IOAuthAuthorizationService
{
    private readonly IOAuthClientStore _clients;
    private readonly IOAuthConsentStore _consents;
    private readonly IOAuthAuthorizationCodeStore _codes;
    private readonly IOAuthEffectivePermissionResolver _permissions;
    private readonly OAuthAuthorizationServerOptions _options;

    public OAuthAuthorizationService(
        IOAuthClientStore clients,
        IOAuthConsentStore consents,
        IOAuthAuthorizationCodeStore codes,
        IOAuthEffectivePermissionResolver permissions,
        IOptions<OAuthAuthorizationServerOptions> options)
    {
        _clients = clients;
        _consents = consents;
        _codes = codes;
        _permissions = permissions;
        _options = options.Value;
        _options.Validate();
    }

    public async Task<OAuthAuthorizationContext> PrepareAsync(
        ClaimsPrincipal subject,
        OAuthAuthorizationRequest request,
        CancellationToken ct = default)
    {
        var evaluated = await EvaluateAsync(subject, request, ct).ConfigureAwait(false);
        var previous = evaluated.Consent?.IsActive == true ? evaluated.Consent.Scopes : [];
        var consentRequired = request.Scopes.Any(scope => !previous.Contains(scope, StringComparer.OrdinalIgnoreCase));
        return new(
            evaluated.Client.ClientId,
            evaluated.Client.DisplayName,
            evaluated.TenantId,
            evaluated.UserId,
            request.RedirectUri,
            request.State,
            request.Scopes,
            request.Resource,
            consentRequired,
            previous);
    }

    public async Task<OAuthAuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal subject,
        OAuthAuthorizationDecision decision,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(decision);
        var evaluated = await EvaluateAsync(subject, decision.Request, ct).ConfigureAwait(false);
        if (!decision.Approved)
            return new(decision.Request.RedirectUri, decision.Request.State, Error: "access_denied", ErrorDescription: "The authorization request was denied.");

        var now = DateTimeOffset.UtcNow;
        var consent = new OAuthConsentRecord(
            evaluated.Consent?.ConsentId ?? Guid.NewGuid(),
            evaluated.UserId,
            evaluated.TenantId,
            evaluated.Client.ClientId,
            decision.Request.Resource,
            decision.Request.Scopes,
            evaluated.Consent?.CreatedUtc ?? now,
            now);
        consent = await _consents.UpsertAsync(consent, ct).ConfigureAwait(false);
        var effective = await _permissions.ResolveAsync(new(
            evaluated.TenantId,
            evaluated.Client,
            consent,
            subject,
            decision.Request.Scopes,
            decision.Request.Resource), ct).ConfigureAwait(false);
        if (effective.GrantedScopes.Count == 0 || effective.DeniedScopes.Count > 0)
            return new(decision.Request.RedirectUri, decision.Request.State, Error: "invalid_scope", ErrorDescription: "One or more requested scopes are not permitted.");

        var rawCode = Base64Url(RandomNumberGenerator.GetBytes(32));
        var record = new OAuthAuthorizationCodeRecord(
            Hash(rawCode),
            evaluated.Client.ClientId,
            decision.Request.RedirectUri,
            evaluated.UserId,
            evaluated.TenantId,
            effective.GrantedScopes,
            decision.Request.Resource,
            decision.Request.CodeChallenge,
            OAuthCodeChallengeMethods.S256,
            now,
            now.AddMinutes(_options.AuthorizationCodeLifetimeMinutes));
        await _codes.CreateAsync(record, ct).ConfigureAwait(false);
        return new(decision.Request.RedirectUri, decision.Request.State, Code: rawCode);
    }

    private async Task<OAuthAuthorizationEvaluation> EvaluateAsync(
        ClaimsPrincipal subject,
        OAuthAuthorizationRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(request);
        var userId = ResolveGuid(subject, "uid", ClaimTypes.NameIdentifier, "sub");
        var tenantId = ResolveGuid(subject, "tid", "tenant_id");
        if (userId == Guid.Empty || tenantId == Guid.Empty)
            throw new OAuthProtocolException("access_denied", "An authenticated tenant user is required.");
        if (request.TenantId is { } requestedTenant && requestedTenant != tenantId)
            throw new OAuthProtocolException("access_denied", "The requested tenant is not available.");

        var client = await _clients.GetAsync(request.ClientId, ct).ConfigureAwait(false);
        if (client is null || !client.IsActive)
            throw new OAuthProtocolException("invalid_request", "The OAuth client is invalid.");
        if (!client.MatchesRedirectUri(request.RedirectUri))
            throw new OAuthProtocolException("invalid_request", "The redirect URI is invalid.");
        var trustedRedirect = request.RedirectUri;
        if (client.TenantId is { } clientTenant && clientTenant != tenantId)
            throw Error("access_denied", "The OAuth client is not available for this tenant.", request, trustedRedirect);
        if (!string.Equals(request.ResponseType, "code", StringComparison.Ordinal))
            throw Error("unsupported_response_type", "Only response_type=code is supported.", request, trustedRedirect);
        if (string.IsNullOrWhiteSpace(request.State))
            throw Error("invalid_request", "state is required.", request, trustedRedirect);
        if (!string.Equals(request.CodeChallengeMethod, OAuthCodeChallengeMethods.S256, StringComparison.Ordinal) || !ValidChallenge(request.CodeChallenge))
            throw Error("invalid_request", "A valid PKCE S256 challenge is required.", request, trustedRedirect);
        if (!client.AllowsResource(request.Resource))
            throw Error("invalid_target", "The requested resource is invalid.", request, trustedRedirect);
        if (request.Scopes.Count == 0 || request.Scopes.Any(scope => !client.AllowedScopes.Contains(scope, StringComparer.OrdinalIgnoreCase)))
            throw Error("invalid_scope", "One or more requested scopes are invalid.", request, trustedRedirect);

        var consent = await _consents.GetAsync(userId, tenantId, client.ClientId, request.Resource, ct).ConfigureAwait(false);
        return new(request, client, consent, subject, userId, tenantId);
    }

    private static OAuthProtocolException Error(string error, string description, OAuthAuthorizationRequest request, string redirect) =>
        new(error, description, redirect, request.State);

    private static Guid ResolveGuid(ClaimsPrincipal principal, params string[] types)
    {
        foreach (var type in types)
            if (Guid.TryParse(principal.FindFirst(type)?.Value, out var value) && value != Guid.Empty)
                return value;
        return Guid.Empty;
    }

    private static bool ValidChallenge(string value) =>
        value.Length is >= 43 and <= 128 && value.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or '~');

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
