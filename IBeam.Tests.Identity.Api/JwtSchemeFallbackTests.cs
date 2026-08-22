using IBeam.Identity.Api.Authentication;
using IBeam.Identity.Api.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Tests.Identity.Api;

/// <summary>
/// Covers <c>IBeam.Identity.Api.DependencyInjection.ServiceCollectionExtensions.AcceptsNonJwtSchemeAsync</c> — the check that decides
/// whether the default JWT scheme's "invalid bearer token format" diagnostic should back off and
/// let another scheme in the endpoint's real policy (e.g. an API-key scheme) get a chance, instead
/// of pre-empting the response. Exercises the exact provider (<see cref="RoleIdsAuthorizationPolicyProvider"/>)
/// and default-policy shape <c>AddIBeamIdentityApi</c> registers, without pulling in that whole
/// heavyweight registration (Azure Table/communications config, etc.).
/// </summary>
[TestClass]
public sealed class JwtSchemeFallbackTests
{
    private const string CombinedPolicyName = "CombinedLikeBindryApi";

    private static IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddAuthorization(options =>
        {
            // Mirrors AddIBeamIdentityApi's exact DefaultPolicy — JWT and ApiKey both accepted.
            options.DefaultPolicy = new AuthorizationPolicyBuilder(
                    JwtBearerDefaults.AuthenticationScheme,
                    ApiKeyAuthenticationDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .Build();

            // Mirrors a host application's own combined policy (e.g. Bindry's "BindryApi"/"IBeamMcp").
            options.AddPolicy(CombinedPolicyName, policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, ApiKeyAuthenticationDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser());
        });
        services.AddSingleton<IAuthorizationPolicyProvider, RoleIdsAuthorizationPolicyProvider>();
        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext ContextFor(IServiceProvider services, Endpoint? endpoint)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        if (endpoint is not null)
            context.SetEndpoint(endpoint);
        return context;
    }

    private static Endpoint EndpointWithPolicy(string? policyName)
    {
        var authorizeData = policyName is null
            ? new AuthorizeAttribute()
            : new AuthorizeAttribute(policyName);
        return new Endpoint(null, new EndpointMetadataCollection(authorizeData), "test-endpoint");
    }

    [TestMethod]
    public async Task NoEndpoint_ReturnsFalse_PreservesExistingDiagnosticBehavior()
    {
        var result = await IBeam.Identity.Api.DependencyInjection.ServiceCollectionExtensions.AcceptsNonJwtSchemeAsync(ContextFor(BuildServices(), endpoint: null));

        Assert.IsFalse(result);
    }

    [TestMethod]
    public async Task PlainAuthorizeAttribute_UsesDefaultPolicy_WhichAcceptsApiKeyToo()
    {
        var context = ContextFor(BuildServices(), EndpointWithPolicy(policyName: null));

        var result = await IBeam.Identity.Api.DependencyInjection.ServiceCollectionExtensions.AcceptsNonJwtSchemeAsync(context);

        Assert.IsTrue(result, "The registered DefaultPolicy accepts ApiKey alongside JWT.");
    }

    [TestMethod]
    public async Task NamedCombinedPolicy_AcceptsNonJwtScheme()
    {
        var context = ContextFor(BuildServices(), EndpointWithPolicy(CombinedPolicyName));

        var result = await IBeam.Identity.Api.DependencyInjection.ServiceCollectionExtensions.AcceptsNonJwtSchemeAsync(context);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task DynamicModulePolicy_HasNoExplicitSchemes_SoDoesNotBackOff()
    {
        // RoleIdsAuthorizationPolicyProvider builds RequireModule:/RequireTool:/etc. policies with
        // an empty AuthenticationSchemes list (falls back to the app's single default scheme, JWT)
        // — these stay JWT-only in practice, so the diagnostic should still fire for them.
        var context = ContextFor(BuildServices(), EndpointWithPolicy("RequireModule:widgets:view"));

        var result = await IBeam.Identity.Api.DependencyInjection.ServiceCollectionExtensions.AcceptsNonJwtSchemeAsync(context);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public async Task UnresolvablePolicyName_ReturnsFalse_PreservesExistingDiagnosticBehavior()
    {
        var context = ContextFor(BuildServices(), EndpointWithPolicy("this-policy-does-not-exist"));

        var result = await IBeam.Identity.Api.DependencyInjection.ServiceCollectionExtensions.AcceptsNonJwtSchemeAsync(context);

        Assert.IsFalse(result);
    }
}
