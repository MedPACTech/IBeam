using IBeam.Identity.Exceptions;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Auth;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Identity.Services;

[TestClass]
public sealed class OAuthAuthorizationServerOptionsTests
{
    [TestMethod]
    public void Validate_NormalizesConfiguredPublicClient()
    {
        var options = CreateOptions();

        options.Validate();

        var client = options.Clients.Single();
        Assert.AreEqual("mcp-client", client.ClientId);
        Assert.AreEqual("MCP Client", client.DisplayName);
        Assert.AreEqual(OAuthClientTypes.Public, client.ClientType);
        CollectionAssert.AreEqual(
            new[] { OAuthGrantTypes.AuthorizationCode, OAuthGrantTypes.RefreshToken },
            client.AllowedGrantTypes);
        CollectionAssert.AreEqual(new[] { "tool:mcp", "api-scope:work" }, client.AllowedScopes);
    }

    [TestMethod]
    public void Validate_PublicClientWithoutPkce_Throws()
    {
        var options = CreateOptions();
        options.Clients[0].RequirePkce = false;

        Assert.ThrowsExactly<InvalidOperationException>(() => options.Validate());
    }

    [TestMethod]
    public void Validate_PublicClientWithSecretHash_Throws()
    {
        var options = CreateOptions();
        options.Clients[0].ClientSecretHash = "pbkdf2-sha256:v1:test-hash";

        Assert.ThrowsExactly<InvalidOperationException>(() => options.Validate());
    }

    [TestMethod]
    public void Validate_RemoteHttpRedirectUri_Throws()
    {
        var options = CreateOptions();
        options.Clients[0].RedirectUris = ["http://client.example/callback"];

        Assert.ThrowsExactly<InvalidOperationException>(() => options.Validate());
    }

    [TestMethod]
    public void Validate_UnsupportedGrantType_Throws()
    {
        var options = CreateOptions();
        options.Clients[0].AllowedGrantTypes = ["implicit"];

        Assert.ThrowsExactly<InvalidOperationException>(() => options.Validate());
    }

    [TestMethod]
    public void Validate_DuplicateClientIds_Throws()
    {
        var options = CreateOptions();
        options.Clients.Add(CreateClient());

        Assert.ThrowsExactly<InvalidOperationException>(() => options.Validate());
    }

    [TestMethod]
    public async Task Store_DisabledClientIsReturnedAsInactive()
    {
        var options = CreateOptions();
        options.Clients[0].Status = OAuthClientStatuses.Disabled;
        var store = new InMemoryOAuthClientStore(Options.Create(options));

        var client = await store.GetAsync("mcp-client");

        Assert.IsNotNull(client);
        Assert.IsFalse(client.IsActive);
        Assert.IsNotNull(client.DisabledUtc);
    }

    [TestMethod]
    public async Task Store_RedirectUriMatchingIsExact()
    {
        var store = new InMemoryOAuthClientStore(Options.Create(CreateOptions()));
        var client = await store.GetAsync("mcp-client");

        Assert.IsNotNull(client);
        Assert.IsTrue(client.MatchesRedirectUri("https://client.example/callback"));
        Assert.IsFalse(client.MatchesRedirectUri("https://CLIENT.example/callback"));
        Assert.IsFalse(client.MatchesRedirectUri("https://client.example/callback/"));
    }

    [TestMethod]
    public async Task Store_CreateDuplicateClient_Throws()
    {
        var store = new InMemoryOAuthClientStore(Options.Create(CreateOptions()));
        var existing = await store.GetAsync("mcp-client");

        await Assert.ThrowsExactlyAsync<IdentityValidationException>(() => store.CreateAsync(existing!));
    }

    [TestMethod]
    public void Validate_LoopbackAndNativePublicRedirectUris_AreAllowed()
    {
        var options = CreateOptions();
        options.Clients[0].RedirectUris =
        [
            "http://127.0.0.1:5173/callback",
            "com.example.mcp:/oauth/callback"
        ];

        options.Validate();

        Assert.HasCount(2, options.Clients[0].RedirectUris);
    }

    private static OAuthAuthorizationServerOptions CreateOptions() =>
        new()
        {
            Enabled = true,
            Issuer = "https://identity.example",
            Clients = [CreateClient()]
        };

    private static OAuthClientRegistrationOptions CreateClient() =>
        new()
        {
            ClientId = " mcp-client ",
            DisplayName = " MCP Client ",
            ClientType = " PUBLIC ",
            RedirectUris = ["https://client.example/callback", "https://client.example/callback"],
            AllowedGrantTypes = [" AUTHORIZATION_CODE ", "refresh_token"],
            AllowedScopes = ["tool:mcp", "api-scope:work", "tool:mcp"],
            AllowedResources = ["https://api.example/mcp"],
            RequirePkce = true
        };
}
