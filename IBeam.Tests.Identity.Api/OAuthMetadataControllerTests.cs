using System.Text.Json;
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
    public async Task Metadata_SerializesRfc8414MemberNamesRegardlessOfHostPolicy()
    {
        var sut = CreateController(enabled: true, dynamicRegistration: true);
        var metadata = (OAuthAuthorizationServerMetadata)((OkObjectResult)await sut.Metadata(CancellationToken.None)).Value!;

        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);

        foreach (var name in new[]
        {
            "issuer", "authorization_endpoint", "token_endpoint", "revocation_endpoint", "jwks_uri",
            "registration_endpoint", "response_types_supported", "grant_types_supported",
            "code_challenge_methods_supported", "token_endpoint_auth_methods_supported",
            "scopes_supported", "resource_indicators_supported"
        })
        {
            Assert.IsTrue(document.RootElement.TryGetProperty(name, out _), $"Missing RFC member {name}");
        }
        Assert.IsFalse(json.Contains("authorizationEndpoint", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Metadata_OmitsRegistrationEndpointWhenDynamicRegistrationDisabled()
    {
        var sut = CreateController(enabled: true, dynamicRegistration: false);
        var metadata = (OAuthAuthorizationServerMetadata)((OkObjectResult)await sut.Metadata(CancellationToken.None)).Value!;

        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);

        Assert.IsFalse(document.RootElement.TryGetProperty("registration_endpoint", out _));
    }

    [TestMethod]
    public async Task ClientMetadata_SerializesRfc7591MemberNames()
    {
        var result = (OkObjectResult)await CreateController(enabled: true).ClientMetadata("client", CancellationToken.None);
        var json = JsonSerializer.Serialize((OAuthClientMetadataDocument)result.Value!, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);

        foreach (var name in new[]
        {
            "client_id", "client_name", "redirect_uris", "grant_types", "scope", "resources", "token_endpoint_auth_method"
        })
        {
            Assert.IsTrue(document.RootElement.TryGetProperty(name, out _), $"Missing RFC member {name}");
        }
    }

    [TestMethod]
    public void RegistrationRequest_BindsRfc7591SnakeCaseMemberNames()
    {
        var request = JsonSerializer.Deserialize<DynamicOAuthClientRegistrationRequest>(
            """{"client_name":"probe","redirect_uris":["https://claude.ai/api/mcp/auth_callback"],"grant_types":["authorization_code"],"scope":"tool:mcp","token_endpoint_auth_method":"none"}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.AreEqual("probe", request.ClientName);
        CollectionAssert.AreEqual(new[] { "https://claude.ai/api/mcp/auth_callback" }, request.RedirectUris);
        CollectionAssert.AreEqual(new[] { OAuthGrantTypes.AuthorizationCode }, request.GrantTypes);
        Assert.AreEqual("tool:mcp", request.Scope);
        Assert.AreEqual("none", request.TokenEndpointAuthMethod);
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
