using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using IBeam.Ai;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Ai;

[TestClass]
public sealed class IBeamMcpOAuthTests
{
    [TestMethod]
    public async Task Metadata_IsPublishedAtRootAndMcpPath()
    {
        var builder = WebApplication.CreateBuilder();
        ConfigureServices(builder.Services);

        await using var app = builder.Build();
        app.MapIBeamMcp("/api/mcp", "AgentApi");

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();

        var root = endpoints.Single(endpoint =>
            endpoint.RoutePattern.RawText == "/.well-known/oauth-protected-resource");
        var path = endpoints.Single(endpoint =>
            endpoint.RoutePattern.RawText == "/.well-known/oauth-protected-resource/api/mcp");

        var rootJson = await InvokeAsync(root, app.Services);
        var pathJson = await InvokeAsync(path, app.Services);

        AssertMetadata(rootJson);
        AssertMetadata(pathJson);
    }

    [TestMethod]
    public async Task Challenge_AppendsBearerMetadataAndPreservesApiKeyChallenge()
    {
        await using var provider = BuildServices();
        var context = CreateMcpContext(provider);
        var handler = provider.GetRequiredService<IAuthorizationMiddlewareResultHandler>();
        var policy = new AuthorizationPolicyBuilder(TestAuthenticationHandler.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .Build();

        await handler.HandleAsync(
            _ => Task.CompletedTask,
            context,
            policy,
            PolicyAuthorizationResult.Challenge());

        Assert.AreEqual(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        var challenges = context.Response.Headers.WWWAuthenticate.ToArray();
        Assert.IsTrue(challenges.Any(value => value!.StartsWith("ApiKey", StringComparison.Ordinal)));
        Assert.IsTrue(challenges.Any(value => value!.Contains(
            "Bearer resource_metadata=\"https://mcp.example.com/.well-known/oauth-protected-resource/api/mcp\"",
            StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Forbid_AppendsInsufficientScopeGuidance()
    {
        await using var provider = BuildServices();
        var context = CreateMcpContext(provider);
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "agent")],
            TestAuthenticationHandler.AuthenticationScheme));
        var handler = provider.GetRequiredService<IAuthorizationMiddlewareResultHandler>();
        var policy = new AuthorizationPolicyBuilder(TestAuthenticationHandler.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .Build();

        await handler.HandleAsync(
            _ => Task.CompletedTask,
            context,
            policy,
            PolicyAuthorizationResult.Forbid());

        Assert.AreEqual(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        var challenge = string.Join(",", context.Response.Headers.WWWAuthenticate.ToArray());
        StringAssert.Contains(challenge, "error=\"insufficient_scope\"");
        StringAssert.Contains(challenge, "scope=\"tool:mcp\"");
        StringAssert.Contains(challenge, "resource_metadata=");
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging();
        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthenticationHandler.AuthenticationScheme;
                options.DefaultChallengeScheme = TestAuthenticationHandler.AuthenticationScheme;
                options.DefaultForbidScheme = TestAuthenticationHandler.AuthenticationScheme;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                TestAuthenticationHandler.AuthenticationScheme,
                _ => { });
        services.AddAuthorization(options =>
            options.AddPolicy("AgentApi", policy =>
                policy.AddAuthenticationSchemes(TestAuthenticationHandler.AuthenticationScheme)
                    .RequireAuthenticatedUser()));
        services.AddIBeamAiMcp(
            configureOAuth: options =>
            {
                options.Enabled = true;
                options.ResourceUri = "https://mcp.example.com/api/mcp";
                options.AuthorizationServerUri = "https://identity.example.com";
                options.ResourceName = "Example MCP";
                options.SupportedScopes = ["tool:mcp", "tool:read"];
            });
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext CreateMcpContext(IServiceProvider services)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new IBeamMcpEndpointMetadata(["tool:mcp"])),
            "IBeam MCP"));
        return context;
    }

    private static async Task<string> InvokeAsync(RouteEndpoint endpoint, IServiceProvider services)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        await using var body = new MemoryStream();
        context.Response.Body = body;

        await endpoint.RequestDelegate!(context);

        body.Position = 0;
        using var reader = new StreamReader(body);
        return await reader.ReadToEndAsync();
    }

    private static void AssertMetadata(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.AreEqual("https://mcp.example.com/api/mcp", root.GetProperty("resource").GetString());
        Assert.AreEqual(
            "https://identity.example.com",
            root.GetProperty("authorization_servers")[0].GetString());
        CollectionAssert.AreEquivalent(
            new[] { "tool:mcp", "tool:read" },
            root.GetProperty("scopes_supported").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.AreEqual("header", root.GetProperty("bearer_methods_supported")[0].GetString());
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string AuthenticationScheme = "TestApiKey";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.NoResult());

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            Response.Headers.Append("WWW-Authenticate", "ApiKey realm=\"ibeam\"");
            return Task.CompletedTask;
        }

        protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
    }
}
