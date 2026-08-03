using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IBeam.Ai;
using IBeam.Identity.Api.DependencyInjection;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Auth;
using IBeam.Identity.Services.Tokens;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Identity.Api;

[TestClass]
public sealed class OAuthMcpEndToEndTests
{
    private const string Issuer = "https://identity.example.com";
    private const string Resource = "https://mcp.example.com/api/mcp";
    private const string RedirectUri = "https://consumer.example.com/oauth/callback";
    private const string Verifier = "a-valid-pkce-verifier-with-more-than-forty-three-characters-123456789";
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [TestMethod]
    public async Task OAuthOnlyConsumer_CompletesDiscoveryPkceRefreshRevokeAndMcpInvocation()
    {
        var client = PublicClient();
        var store = new OAuthMemoryStore(client);
        var jwt = Jwt();
        await using var app = BuildApp(store, jwt);

        var metadata = await InvokeAsync(app, "/.well-known/oauth-protected-resource/api/mcp");
        using (var document = JsonDocument.Parse(metadata))
        {
            Assert.AreEqual(Resource, document.RootElement.GetProperty("resource").GetString());
            Assert.AreEqual(Issuer, document.RootElement.GetProperty("authorization_servers")[0].GetString());
        }

        var authorization = new OAuthAuthorizationService(
            store,
            store,
            store,
            new SubjectPermissionResolver(),
            Options.Create(AuthorizationServerOptions()));
        var subject = Subject();
        var request = AuthorizationRequest(client.ClientId, ["tool:mcp"]);

        var prepared = await authorization.PrepareAsync(subject, request);
        Assert.IsTrue(prepared.ConsentRequired);

        var tenantError = await Assert.ThrowsExactlyAsync<OAuthProtocolException>(() =>
            authorization.PrepareAsync(subject, request with { TenantId = Guid.NewGuid() }));
        Assert.AreEqual("access_denied", tenantError.Error);

        var excessive = await authorization.AuthorizeAsync(
            subject,
            new(AuthorizationRequest(client.ClientId, ["tool:mcp", "tool:admin"]), true));
        Assert.AreEqual("invalid_scope", excessive.Error);

        var approved = await authorization.AuthorizeAsync(subject, new(request, true));
        Assert.IsNotNull(approved.Code);

        var tokens = TokenService(store, jwt);
        var issued = await tokens.ExchangeAsync(new(
            OAuthGrantTypes.AuthorizationCode,
            client.ClientId,
            Code: approved.Code,
            RedirectUri: RedirectUri,
            CodeVerifier: Verifier,
            Resource: Resource));
        Assert.IsNotNull(issued.RefreshToken);

        var oauthPrincipal = await AuthenticateAsync(app.Services, issued.AccessToken);
        var oauthResponse = await InvokeAsync(app, "/api/mcp", oauthPrincipal, ToolRequest());
        StringAssert.Contains(oauthResponse, "oauth-e2e-ok");

        var apiKeyPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("role", "tool:mcp"), new Claim(AgentClaimTypes.ApiCredentialId, Guid.NewGuid().ToString("D"))],
            "IBeamApiKey",
            ClaimTypes.Name,
            "role"));
        var apiKeyResponse = await InvokeAsync(app, "/api/mcp", apiKeyPrincipal, ToolRequest());
        StringAssert.Contains(apiKeyResponse, "oauth-e2e-ok");

        var rotated = await tokens.ExchangeAsync(new(
            OAuthGrantTypes.RefreshToken,
            client.ClientId,
            RefreshToken: issued.RefreshToken));
        Assert.IsNotNull(rotated.RefreshToken);
        Assert.AreNotEqual(issued.RefreshToken, rotated.RefreshToken);

        var replay = await Assert.ThrowsExactlyAsync<OAuthProtocolException>(() =>
            tokens.ExchangeAsync(new(
                OAuthGrantTypes.RefreshToken,
                client.ClientId,
                RefreshToken: issued.RefreshToken)));
        Assert.AreEqual("invalid_grant", replay.Error);

        await tokens.RevokeAsync(new(rotated.RefreshToken!, client.ClientId));
        var revoked = await Assert.ThrowsExactlyAsync<OAuthProtocolException>(() =>
            tokens.ExchangeAsync(new(
                OAuthGrantTypes.RefreshToken,
                client.ClientId,
                RefreshToken: rotated.RefreshToken)));
        Assert.AreEqual("invalid_grant", revoked.Error);
    }

    [TestMethod]
    public async Task ConfidentialConsumer_CompletesClientCredentialsAndMcpInvocation()
    {
        var client = ConfidentialClient();
        var store = new OAuthMemoryStore(client);
        var jwt = Jwt();
        await using var app = BuildApp(store, jwt);

        var issued = await TokenService(store, jwt).ExchangeAsync(new(
            OAuthGrantTypes.ClientCredentials,
            client.ClientId,
            "client-secret",
            Resource: Resource,
            Scopes: ["tool:mcp"]));

        Assert.IsNull(issued.RefreshToken);
        var principal = await AuthenticateAsync(app.Services, issued.AccessToken);
        var response = await InvokeAsync(app, "/api/mcp", principal, ToolRequest());
        StringAssert.Contains(response, "oauth-e2e-ok");
    }

    private static WebApplication BuildApp(OAuthMemoryStore store, JwtOptions jwt)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IOptions<JwtOptions>>(Options.Create(jwt));
        builder.Services.AddSingleton<IJwtSigningKeyProvider, JwtSigningKeyProvider>();
        builder.Services.AddSingleton<IOAuthClientStore>(store);
        builder.Services.AddIBeamAiMcp(
            tools => tools.AddTool(
                "e2e.secure",
                "OAuth compatibility test tool.",
                AgentToolSchemas.EmptyObject(),
                (_, _, _) => ValueTask.FromResult<object?>(new { result = "oauth-e2e-ok" }),
                requiredScopes: ["tool:mcp"]),
            oauth =>
            {
                oauth.Enabled = true;
                oauth.ResourceUri = Resource;
                oauth.AuthorizationServerUri = Issuer;
                oauth.RequiredScope = "tool:mcp";
                oauth.SupportedScopes = ["tool:mcp"];
            });
        builder.Services.AddIBeamMcpAuthorization();

        var app = builder.Build();
        app.MapIBeamMcp("/api/mcp", IBeamMcpAuthenticationDefaults.AuthorizationPolicy);
        return app;
    }

    private static OAuthTokenService TokenService(OAuthMemoryStore store, JwtOptions jwt) => new(
        store,
        store,
        store,
        store,
        new TestSecretHasher(),
        new JwtSigningKeyProvider(Options.Create(jwt)),
        Options.Create(jwt));

    private static async Task<ClaimsPrincipal> AuthenticateAsync(IServiceProvider services, string accessToken)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Headers.Authorization = $"Bearer {accessToken}";
        var result = await context.AuthenticateAsync(IBeamMcpAuthenticationDefaults.OAuthAuthenticationScheme);
        Assert.IsTrue(result.Succeeded, result.Failure?.Message);
        return result.Principal!;
    }

    private static async Task<string> InvokeAsync(
        WebApplication app,
        string pattern,
        ClaimsPrincipal? principal = null,
        string? requestBody = null)
    {
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == pattern);
        var context = new DefaultHttpContext
        {
            RequestServices = app.Services,
            User = principal ?? new ClaimsPrincipal(new ClaimsIdentity())
        };
        if (requestBody is not null)
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));
        await using var response = new MemoryStream();
        context.Response.Body = response;
        app.Services.GetRequiredService<IHttpContextAccessor>().HttpContext = context;

        await endpoint.RequestDelegate!(context);

        response.Position = 0;
        using var reader = new StreamReader(response);
        return await reader.ReadToEndAsync();
    }

    private static OAuthAuthorizationRequest AuthorizationRequest(string clientId, IReadOnlyList<string> scopes) => new(
        "code",
        clientId,
        RedirectUri,
        "state-123",
        scopes,
        Resource,
        PkceChallenge(Verifier),
        OAuthCodeChallengeMethods.S256,
        TenantId);

    private static ClaimsPrincipal Subject() => new(new ClaimsIdentity(
        [
            new Claim("uid", UserId.ToString("D")),
            new Claim("tid", TenantId.ToString("D")),
            new Claim("role", "tool:mcp")
        ],
        "test"));

    private static OAuthAuthorizationServerOptions AuthorizationServerOptions() => new()
    {
        Enabled = true,
        Issuer = Issuer
    };

    private static JwtOptions Jwt() => new()
    {
        Issuer = Issuer,
        Audience = "identity-api",
        SigningKey = "a-development-signing-key-with-at-least-32-bytes",
        ClockSkewSeconds = 0
    };

    private static OAuthClientRecord PublicClient() => new(
        "oauth-only-consumer",
        TenantId,
        "OAuth-only MCP Consumer",
        OAuthClientTypes.Public,
        [RedirectUri],
        [OAuthGrantTypes.AuthorizationCode, OAuthGrantTypes.RefreshToken],
        ["tool:mcp", "tool:admin"],
        [Resource],
        true,
        OAuthClientStatuses.Active,
        null,
        null,
        DateTimeOffset.UtcNow);

    private static OAuthClientRecord ConfidentialClient() => new(
        "confidential-consumer",
        TenantId,
        "Confidential MCP Consumer",
        OAuthClientTypes.Confidential,
        [],
        [OAuthGrantTypes.ClientCredentials],
        ["tool:mcp"],
        [Resource],
        false,
        OAuthClientStatuses.Active,
        "client-secret-hash",
        "test",
        DateTimeOffset.UtcNow);

    private static string PkceChallenge(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string ToolRequest() =>
        """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"e2e.secure","arguments":{}}}""";

    private sealed class SubjectPermissionResolver : IOAuthEffectivePermissionResolver
    {
        public Task<OAuthEffectivePermissionResult> ResolveAsync(
            OAuthPermissionResolutionRequest request,
            CancellationToken ct = default)
        {
            var granted = request.RequestedScopes
                .Where(scope => request.Subject.Claims.Any(claim =>
                    claim.Type == "role" && string.Equals(claim.Value, scope, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            var denied = request.RequestedScopes
                .Except(granted, StringComparer.OrdinalIgnoreCase)
                .Select(scope => new OAuthScopeDenial(scope, OAuthScopeDenialReasons.SubjectNotAllowed))
                .ToArray();
            return Task.FromResult(new OAuthEffectivePermissionResult(granted, denied, []));
        }
    }

    private sealed class TestSecretHasher : IApiCredentialSecretHasher
    {
        public string Hash(string secret) => "client-secret-hash";

        public bool Verify(string secret, string storedHash) =>
            secret == "client-secret" && storedHash == "client-secret-hash";
    }

    private sealed class OAuthMemoryStore(OAuthClientRecord client) :
        IOAuthClientStore,
        IOAuthAuthorizationCodeStore,
        IOAuthConsentStore,
        IAuthSessionStore
    {
        private OAuthAuthorizationCodeRecord? _code;
        private OAuthConsentRecord? _consent;
        private readonly Dictionary<string, AuthSessionRecord> _sessions = new(StringComparer.Ordinal);

        public Task<OAuthClientRecord?> GetAsync(string clientId, CancellationToken ct = default) =>
            Task.FromResult<OAuthClientRecord?>(clientId == client.ClientId ? client : null);

        public Task<IReadOnlyList<OAuthClientRecord>> ListByTenantAsync(Guid? tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OAuthClientRecord>>([client]);

        public Task<OAuthClientRecord> CreateAsync(OAuthClientRecord value, CancellationToken ct = default) =>
            Task.FromResult(value);

        public Task<OAuthClientRecord> UpdateAsync(OAuthClientRecord value, CancellationToken ct = default) =>
            Task.FromResult(value);

        public Task<OAuthAuthorizationCodeRecord> CreateAsync(OAuthAuthorizationCodeRecord code, CancellationToken ct = default)
        {
            _code = code;
            return Task.FromResult(code);
        }

        public Task<OAuthAuthorizationCodeRecord?> GetByHashAsync(string codeHash, CancellationToken ct = default) =>
            Task.FromResult(_code?.CodeHash == codeHash ? _code : null);

        public Task<OAuthAuthorizationCodeRecord?> TryConsumeAsync(string codeHash, DateTimeOffset consumedUtc, CancellationToken ct = default)
        {
            if (_code?.CodeHash != codeHash || _code.ConsumedUtc is not null)
                return Task.FromResult<OAuthAuthorizationCodeRecord?>(null);
            _code = _code with { ConsumedUtc = consumedUtc };
            return Task.FromResult<OAuthAuthorizationCodeRecord?>(_code);
        }

        public Task<OAuthConsentRecord?> GetAsync(Guid userId, Guid tenantId, string clientId, string resource, CancellationToken ct = default) =>
            Task.FromResult(_consent?.UserId == userId && _consent.TenantId == tenantId &&
                            _consent.ClientId == clientId && _consent.Resource == resource
                ? _consent
                : null);

        public Task<OAuthConsentRecord> UpsertAsync(OAuthConsentRecord consent, CancellationToken ct = default)
        {
            _consent = consent;
            return Task.FromResult(consent);
        }

        public Task<bool> RevokeAsync(Guid userId, Guid tenantId, string clientId, string resource, DateTimeOffset revokedUtc, CancellationToken ct = default)
        {
            var matched = _consent?.UserId == userId && _consent.TenantId == tenantId &&
                          _consent.ClientId == clientId && _consent.Resource == resource;
            if (matched)
                _consent = _consent! with { RevokedUtc = revokedUtc };
            return Task.FromResult(matched);
        }

        public Task SaveAsync(AuthSessionRecord record, CancellationToken ct = default)
        {
            _sessions[record.RefreshTokenHash] = record;
            return Task.CompletedTask;
        }

        public Task<AuthSessionRecord?> GetByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken ct = default) =>
            Task.FromResult(_sessions.GetValueOrDefault(refreshTokenHash));

        public Task DeleteByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken ct = default)
        {
            _sessions.Remove(refreshTokenHash);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuthSessionRecord>> GetByUserAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AuthSessionRecord>>(_sessions.Values.Where(value => value.UserId == userId).ToArray());

        public Task<bool> RevokeBySessionIdAsync(Guid userId, string sessionId, CancellationToken ct = default)
        {
            var keys = _sessions
                .Where(pair => pair.Value.UserId == userId && pair.Value.SessionId == sessionId)
                .Select(pair => pair.Key)
                .ToArray();
            foreach (var key in keys)
                _sessions[key] = _sessions[key] with { RevokedAt = DateTimeOffset.UtcNow };
            return Task.FromResult(keys.Length > 0);
        }
    }
}
