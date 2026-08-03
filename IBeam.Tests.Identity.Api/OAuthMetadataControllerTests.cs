using IBeam.Identity.Api.Controllers;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Identity.Api;

[TestClass]
public sealed class OAuthMetadataControllerTests
{
    [TestMethod]
    public async Task Metadata_PublishesAbsoluteEnabledCapabilities()
    {
        var sut = CreateController(enabled: true, dynamicRegistration: true);

        var result = (OkObjectResult)await sut.Metadata(CancellationToken.None);
        var metadata = (OAuthAuthorizationServerMetadata)result.Value!;

        Assert.AreEqual("https://identity.example.test/oauth/authorize", metadata.AuthorizationEndpoint);
        Assert.AreEqual("https://identity.example.test/oauth/register", metadata.RegistrationEndpoint);
        CollectionAssert.Contains(metadata.ScopesSupported.ToList(), "tool:mcp");
        CollectionAssert.Contains(metadata.GrantTypesSupported.ToList(), OAuthGrantTypes.ClientCredentials);
        CollectionAssert.AreEqual(new[] { "S256" }, metadata.CodeChallengeMethodsSupported.ToArray());
    }

    [TestMethod]
    public async Task Metadata_ReturnsNotFoundWhenServerDisabled()
    {
        var result = await CreateController(enabled: false).Metadata(CancellationToken.None);
        Assert.IsInstanceOfType<NotFoundResult>(result);
    }

    [TestMethod]
    public async Task ClientMetadata_NeverContainsSecretMaterial()
    {
        var result = (OkObjectResult)await CreateController(enabled: true).ClientMetadata("client", CancellationToken.None);
        var document = (OAuthClientMetadataDocument)result.Value!;

        Assert.AreEqual("client", document.ClientId);
        Assert.IsNull(typeof(OAuthClientMetadataDocument).GetProperty("ClientSecret"));
        Assert.IsNull(typeof(OAuthClientMetadataDocument).GetProperty("ClientSecretHash"));
    }

    [TestMethod]
    public async Task Register_ReturnsNotFoundWhenCompatibilityDisabled()
    {
        var result = await CreateController(enabled: true).Register(new(), CancellationToken.None);
        Assert.IsInstanceOfType<NotFoundResult>(result);
    }

    private static OAuthMetadataController CreateController(bool enabled, bool dynamicRegistration = false)
    {
        var controller = new OAuthMetadataController(
            Options.Create(new OAuthAuthorizationServerOptions
            {
                Enabled = enabled,
                Issuer = enabled ? "https://identity.example.test" : string.Empty,
                DynamicClientRegistrationEnabled = dynamicRegistration
            }),
            new FakeScopeCatalog(),
            new FakeClientStore())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return controller;
    }

    private sealed class FakeScopeCatalog : IApiCredentialScopeCatalogProvider
    {
        public Task<IReadOnlyList<ApiScopeCatalogItem>> GetScopesAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ApiScopeCatalogItem>>([
                new("mcp", "MCP", "", "tool", true, false),
                new("work", "Work", "", "module", true, true)
            ]);
    }

    private sealed class FakeClientStore : IOAuthClientStore
    {
        private readonly OAuthClientRecord _client = new(
            "client", null, "Consumer", OAuthClientTypes.Confidential, ["https://app.example/callback"],
            [OAuthGrantTypes.AuthorizationCode], ["tool:mcp"], ["https://mcp.example"], true,
            OAuthClientStatuses.Active, "private-hash", "hash", DateTimeOffset.UtcNow);
        public Task<OAuthClientRecord?> GetAsync(string clientId, CancellationToken ct = default) => Task.FromResult<OAuthClientRecord?>(clientId == "client" ? _client : null);
        public Task<IReadOnlyList<OAuthClientRecord>> ListByTenantAsync(Guid? tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OAuthClientRecord> CreateAsync(OAuthClientRecord client, CancellationToken ct = default) => Task.FromResult(client);
        public Task<OAuthClientRecord> UpdateAsync(OAuthClientRecord client, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
