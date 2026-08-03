using System.Security.Claims;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Services.Authorization;
using Moq;

namespace IBeam.Tests.Identity.Services;

[TestClass]
public sealed class OAuthEffectivePermissionResolverTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string ClientId = "consumer-app";
    private const string Resource = "https://mcp.example.test";

    [TestMethod]
    public async Task ResolveAsync_GrantsAllowedScopeAndCreatesNormalizedClaims()
    {
        var resolver = CreateResolver();
        var result = await resolver.ResolveAsync(CreateRequest(
            requested: ["api-scope:work", "tool:mcp"],
            clientScopes: ["api-scope:work", "tool:mcp"],
            consentScopes: ["api-scope:work", "tool:mcp"],
            subjectRoles: ["api-scope:work", "tool:mcp"]));

        CollectionAssert.AreEquivalent(
            new[] { "api-scope:work", "tool:mcp" },
            result.GrantedScopes.ToArray());
        Assert.IsEmpty(result.DeniedScopes);
        Assert.IsTrue(result.Claims.Contains(new ClaimItem("scope", "work")));
        Assert.IsTrue(result.Claims.Contains(new ClaimItem("tool", "mcp")));
        Assert.IsTrue(result.Claims.Contains(new ClaimItem("tenant_id", TenantId.ToString("D"))));
        Assert.IsTrue(result.Claims.Contains(new ClaimItem("resource", Resource)));
    }

    [TestMethod]
    public async Task ResolveAsync_ReturnsOnlyIntersectionOfRequestedAndClientScopes()
    {
        var result = await CreateResolver().ResolveAsync(CreateRequest(
            requested: ["api-scope:work", "tool:mcp"],
            clientScopes: ["api-scope:work"],
            consentScopes: ["api-scope:work", "tool:mcp"],
            subjectRoles: ["api-scope:work", "tool:mcp"]));

        CollectionAssert.AreEqual(new[] { "api-scope:work" }, result.GrantedScopes.ToArray());
        Assert.AreEqual(OAuthScopeDenialReasons.ClientNotAllowed, result.DeniedScopes.Single().Reason);
    }

    [TestMethod]
    public async Task ResolveAsync_AppliesWildcardOnlyToWildcardCapableCatalogEntries()
    {
        var result = await CreateResolver().ResolveAsync(CreateRequest(
            requested: ["api-scope:work", "tool:mcp"],
            clientScopes: ["api-scope:*", "tool:*"],
            consentScopes: ["api-scope:*", "tool:*"],
            subjectRoles: ["api-scope:*", "tool:*"]));

        CollectionAssert.AreEqual(new[] { "api-scope:work" }, result.GrantedScopes.ToArray());
        Assert.AreEqual("tool:mcp", result.DeniedScopes.Single().Scope);
        Assert.AreEqual(OAuthScopeDenialReasons.ClientNotAllowed, result.DeniedScopes.Single().Reason);
    }

    [TestMethod]
    public async Task ResolveAsync_RejectsCrossTenantSubject()
    {
        var otherTenant = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var result = await CreateResolver().ResolveAsync(CreateRequest(
            requested: ["api-scope:work"],
            clientScopes: ["api-scope:work"],
            consentScopes: ["api-scope:work"],
            subjectRoles: ["api-scope:work"],
            subjectTenantId: otherTenant));

        Assert.IsEmpty(result.GrantedScopes);
        Assert.AreEqual(OAuthScopeDenialReasons.TenantMismatch, result.DeniedScopes.Single().Reason);
    }

    [TestMethod]
    public async Task ResolveAsync_ReportsUnknownScopeWithoutElevation()
    {
        var result = await CreateResolver().ResolveAsync(CreateRequest(
            requested: ["api-scope:unknown"],
            clientScopes: ["api-scope:unknown"],
            consentScopes: ["api-scope:unknown"],
            subjectRoles: ["api-scope:*"]));

        Assert.IsEmpty(result.GrantedScopes);
        Assert.AreEqual(OAuthScopeDenialReasons.UnknownScope, result.DeniedScopes.Single().Reason);
    }

    [TestMethod]
    public async Task ResolveAsync_RequiresActiveConsent()
    {
        var request = CreateRequest(
            requested: ["api-scope:work"],
            clientScopes: ["api-scope:work"],
            consentScopes: ["api-scope:work"],
            subjectRoles: ["api-scope:work"]);

        var result = await CreateResolver().ResolveAsync(request with { Consent = null });

        Assert.IsEmpty(result.GrantedScopes);
        Assert.AreEqual(OAuthScopeDenialReasons.ConsentRequired, result.DeniedScopes.Single().Reason);
    }

    [TestMethod]
    public async Task ResolveAsync_AppliesTenantAllowAndDenyPolicy()
    {
        var result = await CreateResolver().ResolveAsync(CreateRequest(
            requested: ["api-scope:work", "tool:mcp"],
            clientScopes: ["api-scope:work", "tool:mcp"],
            consentScopes: ["api-scope:work", "tool:mcp"],
            subjectRoles: ["api-scope:work", "tool:mcp"]) with
        {
            TenantPolicy = new(["api-scope:*", "tool:mcp"], ["tool:mcp"])
        });

        CollectionAssert.AreEqual(new[] { "api-scope:work" }, result.GrantedScopes.ToArray());
        Assert.AreEqual(OAuthScopeDenialReasons.TenantPolicyDenied, result.DeniedScopes.Single().Reason);
    }

    [TestMethod]
    public async Task ResolveAsync_ReusesEffectivePermissionAccessAndNormalizesPermissionClaim()
    {
        var permission = new AccessCatalogItem(
            "cards.write", "Write cards", null, AccessCatalogCategories.Permission,
            AccessCatalogSources.IBeamDefault, true, false, true);
        var catalog = EmptyCatalog() with { Permissions = [permission] };
        var result = await CreateResolver(catalog, hasPermission: true).ResolveAsync(CreateRequest(
            requested: ["permission:cards.write"],
            clientScopes: ["permission:cards.write"],
            consentScopes: ["permission:cards.write"],
            subjectRoles: []));

        CollectionAssert.AreEqual(new[] { "permission:cards.write" }, result.GrantedScopes.ToArray());
        Assert.IsTrue(result.Claims.Contains(new ClaimItem("permission", "cards.write")));
    }

    private static OAuthEffectivePermissionResolver CreateResolver(
        AccessCatalogDto? accessCatalog = null,
        bool hasPermission = false)
    {
        var scopes = new Mock<IApiCredentialScopeCatalogProvider>();
        scopes.Setup(x => x.GetScopesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ApiScopeCatalogItem("work", "Work", "", "module", true, true, ModuleKey: "work"),
                new ApiScopeCatalogItem("mcp", "MCP", "", "tool", true, false)
            ]);

        var access = new Mock<IIBeamAccessControlService>();
        access.Setup(x => x.GetAccessCatalogAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(accessCatalog ?? EmptyCatalog());
        access.Setup(x => x.HasPermissionAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasPermission);

        return new OAuthEffectivePermissionResolver(scopes.Object, access.Object);
    }

    private static OAuthPermissionResolutionRequest CreateRequest(
        IReadOnlyList<string> requested,
        IReadOnlyList<string> clientScopes,
        IReadOnlyList<string> consentScopes,
        IReadOnlyList<string> subjectRoles,
        Guid? subjectTenantId = null)
    {
        var now = DateTimeOffset.UtcNow;
        var claims = new List<Claim>
        {
            new("tid", (subjectTenantId ?? TenantId).ToString("D")),
            new("sub", UserId.ToString("D"))
        };
        claims.AddRange(subjectRoles.Select(role => new Claim("role", role)));

        var client = new OAuthClientRecord(
            ClientId, TenantId, "Consumer", OAuthClientTypes.Public, ["https://app.example.test/callback"],
            [OAuthGrantTypes.AuthorizationCode], clientScopes, [Resource], true, OAuthClientStatuses.Active,
            null, null, now);
        var consent = new OAuthConsentRecord(
            Guid.NewGuid(), UserId, TenantId, ClientId, Resource, consentScopes, now, now);

        return new OAuthPermissionResolutionRequest(
            TenantId,
            client,
            consent,
            new ClaimsPrincipal(new ClaimsIdentity(claims, "test", "name", "role")),
            requested,
            Resource);
    }

    private static AccessCatalogDto EmptyCatalog() => new(
        [], [], [], [], [], [], [], []);
}
