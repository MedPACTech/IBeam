using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using IBeam.Ai;
using IBeam.Identity.Api.DependencyInjection;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Tokens;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Identity.Api;

[TestClass]
public sealed class McpAuthorizationTests
{
    private const string Issuer = "https://identity.example.com";
    private const string Resource = "https://mcp.example.com/api/mcp";
    private const string ClientId = "mcp-client";
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [TestMethod]
    public async Task OAuthToken_AuthenticatesAndAuthorizesWithNormalizedClaims()
    {
        await using var provider = CreateServices(Client());
        var result = await AuthenticateAsync(provider, CreateToken(Resource, includeScope: true));

        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(result.Principal!.Claims.Any(claim => claim.Type == "role" && claim.Value == "tool:mcp"));
        Assert.IsTrue(result.Principal.Claims.Any(claim => claim.Type == "scope" && claim.Value == "tool:mcp"));

        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var authorized = await authorization.AuthorizeAsync(
            result.Principal,
            null,
            IBeamMcpAuthenticationDefaults.AuthorizationPolicy);
        Assert.IsTrue(authorized.Succeeded);
    }

    [TestMethod]
    public async Task OAuthToken_RejectsWrongAudience()
    {
        await using var provider = CreateServices(Client());
        var result = await AuthenticateAsync(
            provider,
            CreateToken("https://other.example.com/api/mcp", includeScope: true));

        Assert.IsFalse(result.Succeeded);
    }

    [TestMethod]
    public async Task OAuthToken_RejectsDisabledClient()
    {
        await using var provider = CreateServices(Client() with
        {
            Status = OAuthClientStatuses.Disabled,
            DisabledUtc = DateTimeOffset.UtcNow
        });
        var result = await AuthenticateAsync(provider, CreateToken(Resource, includeScope: true));

        Assert.IsFalse(result.Succeeded);
    }

    [TestMethod]
    public async Task OAuthToken_WithoutRequiredScopeIsForbidden()
    {
        await using var provider = CreateServices(Client());
        var result = await AuthenticateAsync(provider, CreateToken(Resource, includeScope: false));
        Assert.IsTrue(result.Succeeded);

        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var authorized = await authorization.AuthorizeAsync(
            result.Principal!,
            null,
            IBeamMcpAuthenticationDefaults.AuthorizationPolicy);
        Assert.IsFalse(authorized.Succeeded);
    }

    [TestMethod]
    public async Task ApiKeyPrincipal_UsesTheSameMcpScopePolicy()
    {
        await using var provider = CreateServices(Client());
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("role", "tool:mcp")],
            "IBeamApiKey",
            ClaimTypes.Name,
            "role"));

        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var result = await authorization.AuthorizeAsync(
            principal,
            null,
            IBeamMcpAuthenticationDefaults.AuthorizationPolicy);

        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void OAuthContext_ExposesClientAndSessionWithoutApiCredentialId()
    {
        var services = new ServiceCollection();
        services.AddHttpContextAccessor();
        services.AddScoped<IAgentToolContextFactory, HttpAgentToolContextFactory>();
        using var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim("client_id", ClientId),
                    new Claim("sid", "session-1"),
                    new Claim("tid", TenantId.ToString("D"))
                ],
                IBeamMcpAuthenticationDefaults.OAuthAuthenticationScheme))
        };

        var context = provider.GetRequiredService<IAgentToolContextFactory>().Create(provider);

        Assert.AreEqual(ClientId, context.OAuthClientId);
        Assert.AreEqual("session-1", context.OAuthSessionId);
        Assert.IsNull(context.ApiCredentialId);
    }

    private static ServiceProvider CreateServices(OAuthClientRecord client)
    {
        var jwt = new JwtOptions
        {
            Issuer = Issuer,
            Audience = "identity-api",
            SigningKey = "a-development-signing-key-with-at-least-32-bytes",
            ClockSkewSeconds = 0
        };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton<IOptions<JwtOptions>>(Options.Create(jwt));
        services.AddSingleton<IJwtSigningKeyProvider, JwtSigningKeyProvider>();
        services.AddSingleton<IOAuthClientStore>(new FakeClientStore(client));
        services.Configure<IBeamMcpOAuthOptions>(options =>
        {
            options.Enabled = true;
            options.ResourceUri = Resource;
            options.AuthorizationServerUri = Issuer;
            options.RequiredScope = "tool:mcp";
            options.SupportedScopes = ["tool:mcp"];
        });
        services.AddIBeamMcpAuthorization();
        return services.BuildServiceProvider();
    }

    private static async Task<AuthenticateResult> AuthenticateAsync(IServiceProvider services, string token)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Headers.Authorization = $"Bearer {token}";
        return await context.AuthenticateAsync(IBeamMcpAuthenticationDefaults.OAuthAuthenticationScheme);
    }

    private static string CreateToken(string audience, bool includeScope)
    {
        var claims = new List<Claim>
        {
            new("sub", "agent-user"),
            new("tid", TenantId.ToString("D")),
            new("client_id", ClientId),
            new("azp", ClientId),
            new("resource", audience),
            new("sid", "session-1"),
            new("jti", Guid.NewGuid().ToString("D"))
        };
        if (includeScope)
            claims.Add(new Claim("tool", "mcp"));

        var options = Options.Create(new JwtOptions
        {
            Issuer = Issuer,
            Audience = "identity-api",
            SigningKey = "a-development-signing-key-with-at-least-32-bytes"
        });
        var signingKeys = new JwtSigningKeyProvider(options);
        var token = new JwtSecurityToken(
            Issuer,
            audience,
            claims,
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(5),
            signingKeys.SigningCredentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static OAuthClientRecord Client() => new(
        ClientId,
        TenantId,
        "MCP Client",
        OAuthClientTypes.Confidential,
        [],
        [OAuthGrantTypes.ClientCredentials],
        ["tool:mcp"],
        [Resource],
        false,
        OAuthClientStatuses.Active,
        "hash",
        "pbkdf2-sha256",
        DateTimeOffset.UtcNow);

    private sealed class FakeClientStore(OAuthClientRecord client) : IOAuthClientStore
    {
        public Task<OAuthClientRecord?> GetAsync(string clientId, CancellationToken ct = default) =>
            Task.FromResult<OAuthClientRecord?>(clientId == client.ClientId ? client : null);

        public Task<IReadOnlyList<OAuthClientRecord>> ListByTenantAsync(Guid? tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OAuthClientRecord> CreateAsync(OAuthClientRecord value, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OAuthClientRecord> UpdateAsync(OAuthClientRecord value, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
