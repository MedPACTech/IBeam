using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Auth;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Services.Tokens;

public sealed class OAuthTokenService : IOAuthTokenService
{
    private readonly IOAuthClientStore _clients;
    private readonly IOAuthAuthorizationCodeStore _codes;
    private readonly IOAuthConsentStore _consents;
    private readonly IAuthSessionStore _sessions;
    private readonly IApiCredentialSecretHasher _secretHasher;
    private readonly IJwtSigningKeyProvider _signingKeys;
    private readonly JwtOptions _jwt;
    private readonly JwtSecurityTokenHandler _handler = new();

    public OAuthTokenService(
        IOAuthClientStore clients,
        IOAuthAuthorizationCodeStore codes,
        IOAuthConsentStore consents,
        IAuthSessionStore sessions,
        IApiCredentialSecretHasher secretHasher,
        IJwtSigningKeyProvider signingKeys,
        IOptions<JwtOptions> jwt)
    {
        _clients = clients;
        _codes = codes;
        _consents = consents;
        _sessions = sessions;
        _secretHasher = secretHasher;
        _signingKeys = signingKeys;
        _jwt = jwt.Value;
        _jwt.Validate();
    }

    public async Task<OAuthTokenResponse> ExchangeAsync(OAuthTokenRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var client = await AuthenticateClientAsync(request.ClientId, request.ClientSecret, ct).ConfigureAwait(false);
        if (!client.AllowsGrantType(request.GrantType))
            throw Error("unauthorized_client", "The client cannot use this grant type.");

        return request.GrantType switch
        {
            OAuthGrantTypes.AuthorizationCode => await ExchangeCodeAsync(client, request, ct).ConfigureAwait(false),
            OAuthGrantTypes.RefreshToken => await RefreshAsync(client, request, ct).ConfigureAwait(false),
            OAuthGrantTypes.ClientCredentials => await ClientCredentialsAsync(client, request, ct).ConfigureAwait(false),
            _ => throw Error("unsupported_grant_type", "The grant type is not supported.")
        };
    }

    public async Task RevokeAsync(OAuthRevocationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var client = await AuthenticateClientAsync(request.ClientId, request.ClientSecret, ct).ConfigureAwait(false);
        var hash = Hash(request.Token);
        var session = await _sessions.GetByRefreshTokenHashAsync(hash, ct).ConfigureAwait(false);
        if (session is not null && ClaimValue(session.ClaimsJson, "client_id") == client.ClientId)
        {
            await _sessions.RevokeBySessionIdAsync(session.UserId, session.SessionId, ct).ConfigureAwait(false);
            if (request.RevokeConsent && client.TenantId is { } tenantId && !string.IsNullOrWhiteSpace(request.Resource))
                await _consents.RevokeAsync(session.UserId, tenantId, client.ClientId, request.Resource, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        }
    }

    private async Task<OAuthTokenResponse> ExchangeCodeAsync(OAuthClientRecord client, OAuthTokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code)) throw Error("invalid_grant", "The authorization code is invalid.");
        var code = await _codes.GetByHashAsync(OAuthAuthorizationService.Hash(request.Code), ct).ConfigureAwait(false);
        if (code is null || !code.IsUsable(DateTimeOffset.UtcNow) || code.ClientId != client.ClientId ||
            !string.Equals(code.RedirectUri, request.RedirectUri, StringComparison.Ordinal) ||
            !string.Equals(code.Resource, request.Resource, StringComparison.Ordinal) ||
            !VerifyPkce(request.CodeVerifier, code.CodeChallenge))
            throw Error("invalid_grant", "The authorization code is invalid.");

        var consumed = await _codes.TryConsumeAsync(code.CodeHash, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        if (consumed is null) throw Error("invalid_grant", "The authorization code is invalid.");
        var claims = ScopeClaims(code.Scopes, code.UserId.ToString("D"), code.TenantId, client.ClientId, code.Resource);
        var allowRefresh = client.AllowsGrantType(OAuthGrantTypes.RefreshToken);
        return await IssueAsync(code.UserId, code.TenantId, client, code.Resource, claims, allowRefresh, null, ct).ConfigureAwait(false);
    }

    private async Task<OAuthTokenResponse> ClientCredentialsAsync(OAuthClientRecord client, OAuthTokenRequest request, CancellationToken ct)
    {
        if (client.ClientType != OAuthClientTypes.Confidential || client.TenantId is null)
            throw Error("unauthorized_client", "A tenant-scoped confidential client is required.");
        var resource = request.Resource ?? string.Empty;
        if (!client.AllowsResource(resource)) throw Error("invalid_target", "The requested resource is invalid.");
        var scopes = (request.Scopes ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (scopes.Count == 0 || scopes.Any(scope => !client.AllowedScopes.Contains(scope, StringComparer.OrdinalIgnoreCase)))
            throw Error("invalid_scope", "One or more requested scopes are invalid.");
        var machineId = DeterministicGuid(client.ClientId);
        var claims = ScopeClaims(scopes, client.ClientId, client.TenantId.Value, client.ClientId, resource);
        claims.Add(new("principal_type", "oauth-client"));
        return await IssueAsync(machineId, client.TenantId.Value, client, resource, claims, false, null, ct).ConfigureAwait(false);
    }

    private async Task<OAuthTokenResponse> RefreshAsync(OAuthClientRecord client, OAuthTokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken)) throw Error("invalid_grant", "The refresh token is invalid.");
        var oldHash = Hash(request.RefreshToken);
        var session = await _sessions.GetByRefreshTokenHashAsync(oldHash, ct).ConfigureAwait(false);
        if (session is null || session.RevokedAt is not null || session.RefreshTokenExpiresAt <= DateTimeOffset.UtcNow ||
            ClaimValue(session.ClaimsJson, "client_id") != client.ClientId)
            throw Error("invalid_grant", "The refresh token is invalid.");
        var claims = JsonSerializer.Deserialize<List<ClaimItem>>(session.ClaimsJson) ?? [];
        var resource = ClaimValue(claims, "resource") ?? throw Error("invalid_grant", "The refresh token is invalid.");
        await _sessions.DeleteByRefreshTokenHashAsync(oldHash, ct).ConfigureAwait(false);
        return await IssueAsync(session.UserId, session.TenantId, client, resource, claims, true, session.SessionId, ct).ConfigureAwait(false);
    }

    private async Task<OAuthTokenResponse> IssueAsync(Guid userId, Guid tenantId, OAuthClientRecord client, string resource, List<ClaimItem> claims, bool refresh, string? sessionId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(_jwt.AccessTokenMinutes);
        sessionId ??= Guid.NewGuid().ToString("D");
        claims.RemoveAll(x => x.Type is "sid" or "jti");
        claims.Add(new("sid", sessionId));
        claims.Add(new("jti", Guid.NewGuid().ToString("D")));
        var jwtClaims = claims.Select(x => new Claim(x.Type, x.Value, x.ValueType ?? ClaimValueTypes.String)).ToList();
        var token = new JwtSecurityToken(_jwt.Issuer, resource, jwtClaims, now.UtcDateTime, expires.UtcDateTime, _signingKeys.SigningCredentials);
        string? rawRefresh = null;
        if (refresh)
        {
            rawRefresh = Base64Url(RandomNumberGenerator.GetBytes(48));
            await _sessions.SaveAsync(new(
                Hash(rawRefresh), sessionId, userId, tenantId, JsonSerializer.Serialize(claims), now, now,
                now.AddDays(_jwt.RefreshTokenDays)), ct).ConfigureAwait(false);
        }
        var scopes = claims.Where(x => x.Type == "role" && x.Value.Contains(':')).Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase);
        return new(_handler.WriteToken(token), "Bearer", checked((int)(expires - now).TotalSeconds), string.Join(' ', scopes), rawRefresh);
    }

    private async Task<OAuthClientRecord> AuthenticateClientAsync(string clientId, string? secret, CancellationToken ct)
    {
        var client = await _clients.GetAsync(clientId, ct).ConfigureAwait(false);
        if (client is null || !client.IsActive) throw Error("invalid_client", "Client authentication failed.");
        if (client.ClientType == OAuthClientTypes.Confidential &&
            (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(client.ClientSecretHash) || !_secretHasher.Verify(secret, client.ClientSecretHash)))
            throw Error("invalid_client", "Client authentication failed.");
        return client;
    }

    private static List<ClaimItem> ScopeClaims(IEnumerable<string> scopes, string subject, Guid tenantId, string clientId, string resource)
    {
        var claims = new List<ClaimItem> { new("sub", subject), new("tid", tenantId.ToString("D")), new("tenant_id", tenantId.ToString("D")), new("client_id", clientId), new("azp", clientId), new("resource", resource) };
        foreach (var scope in scopes)
        {
            claims.Add(new("role", scope));
            var parts = scope.Split(':', 2);
            if (parts.Length == 2) claims.Add(new(parts[0] switch { "api-scope" => "scope", "tool" => "tool", "permission" => "permission", _ => parts[0] }, parts[1]));
        }
        return claims;
    }

    private static bool VerifyPkce(string? verifier, string challenge) => !string.IsNullOrWhiteSpace(verifier) && Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))) == challenge;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static Guid DeterministicGuid(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value))[..16]);
    private static string? ClaimValue(string json, string type) => ClaimValue(JsonSerializer.Deserialize<List<ClaimItem>>(json) ?? [], type);
    private static string? ClaimValue(IEnumerable<ClaimItem> claims, string type) => claims.FirstOrDefault(x => x.Type == type)?.Value;
    private static OAuthProtocolException Error(string error, string description) => new(error, description);
}
