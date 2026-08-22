using System.Security.Claims;
using IBeam.Identity.Api.Authentication;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IBeam.Tests.Identity.Api;

[TestClass]
public sealed class ApiKeyAuthenticationHandlerTests
{
    private static IServiceProvider BuildServices(IApiCredentialAuthenticator authenticator)
    {
        var services = new ServiceCollection();
        services.AddSingleton(authenticator);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.Configure<ApiCredentialOptions>(_ => { });
        services.AddAuthentication()
            .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationDefaults.AuthenticationScheme, _ => { });
        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext BuildContext(IServiceProvider services, Action<HttpRequest> configureRequest)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        configureRequest(context.Request);
        return context;
    }

    private sealed class StubAuthenticator : IApiCredentialAuthenticator
    {
        public string? ReceivedApiKey { get; private set; }

        public Task<ApiCredentialAuthenticationResult> AuthenticateAsync(
            string apiKey, string? ipAddress = null, CancellationToken ct = default)
        {
            ReceivedApiKey = apiKey;
            var principal = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim("sub", "test")], ApiKeyAuthenticationDefaults.AuthenticationScheme));
            return Task.FromResult(new ApiCredentialAuthenticationResult(true, Principal: principal));
        }
    }

    [TestMethod]
    public async Task XApiKeyHeader_Authenticates()
    {
        var authenticator = new StubAuthenticator();
        var context = BuildContext(BuildServices(authenticator), req => req.Headers["X-API-Key"] = "secret-key-1");

        var result = await context.AuthenticateAsync(ApiKeyAuthenticationDefaults.AuthenticationScheme);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("secret-key-1", authenticator.ReceivedApiKey);
    }

    [TestMethod]
    public async Task AuthorizationApiKeyPrefix_Authenticates()
    {
        var authenticator = new StubAuthenticator();
        var context = BuildContext(BuildServices(authenticator), req => req.Headers.Authorization = "ApiKey secret-key-2");

        var result = await context.AuthenticateAsync(ApiKeyAuthenticationDefaults.AuthenticationScheme);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("secret-key-2", authenticator.ReceivedApiKey);
    }

    [TestMethod]
    public async Task AuthorizationKeyPrefix_Authenticates()
    {
        var authenticator = new StubAuthenticator();
        var context = BuildContext(BuildServices(authenticator), req => req.Headers.Authorization = "Key secret-key-3");

        var result = await context.AuthenticateAsync(ApiKeyAuthenticationDefaults.AuthenticationScheme);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("secret-key-3", authenticator.ReceivedApiKey);
    }

    [TestMethod]
    public async Task AuthorizationBearerPrefix_Authenticates()
    {
        // Bearer is accepted alongside ApiKey/Key so clients whose tooling only supports standard
        // Bearer tokens (e.g. Codex CLI's `codex mcp add --bearer-token-env-var`, which has no
        // custom-header option) can still authenticate against IBeam's API-key scheme.
        var authenticator = new StubAuthenticator();
        var context = BuildContext(BuildServices(authenticator), req => req.Headers.Authorization = "Bearer secret-key-4");

        var result = await context.AuthenticateAsync(ApiKeyAuthenticationDefaults.AuthenticationScheme);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("secret-key-4", authenticator.ReceivedApiKey);
    }

    [TestMethod]
    public async Task NoCredential_ReturnsNoResult()
    {
        var authenticator = new StubAuthenticator();
        var context = BuildContext(BuildServices(authenticator), _ => { });

        var result = await context.AuthenticateAsync(ApiKeyAuthenticationDefaults.AuthenticationScheme);

        Assert.IsFalse(result.Succeeded);
        Assert.IsNull(authenticator.ReceivedApiKey);
    }
}
