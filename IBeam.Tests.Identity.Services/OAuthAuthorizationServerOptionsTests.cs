using IBeam.Identity.Exceptions;
using Microsoft.Extensions.Configuration;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Auth;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Identity.Services;

[TestClass]
public sealed class OAuthAuthorizationServerOptionsTests
{
    [TestMethod]
    public void ConfiguredGrantTypes_ReplaceTheDefault_RatherThanAddingToIt()
    {
        // IBM-0072. AllowedGrantTypes used to default to [authorization_code] in its property
        // initializer, and IConfiguration.Bind adds to a collection rather than replacing it — so a
        // client registered for the device flow alone silently also allowed authorization_code, and the
        // config file gave no hint of it. Bound from configuration, not set in code, because an object
        // initializer replaces the list and would pass either way.
        var options = BindFromConfiguration(new Dictionary<string, string?>
        {
            ["Clients:0:ClientId"] = "cli",
            ["Clients:0:ClientType"] = OAuthClientTypes.Public,
            ["Clients:0:DeviceVerificationUri"] = "https://example.test/device",
            ["Clients:0:AllowedGrantTypes:0"] = OAuthGrantTypes.DeviceCode
        });

        options.Validate();

        CollectionAssert.AreEqual(new[] { OAuthGrantTypes.DeviceCode }, options.Clients.Single().AllowedGrantTypes);
    }

    [TestMethod]
    public void AClientThatConfiguresNoGrantTypes_StillGetsTheAuthorizationCodeDefault()
    {
        // The default did not go away, it moved: it is applied after binding instead of before, so
        // configuration can replace it. Zero-config behaviour is unchanged.
        var options = BindFromConfiguration(new Dictionary<string, string?>
        {
            ["Clients:0:ClientId"] = "cli",
            ["Clients:0:ClientType"] = OAuthClientTypes.Public,
            ["Clients:0:RedirectUris:0"] = "https://example.test/callback"
        });

        options.Validate();

        CollectionAssert.AreEqual(new[] { OAuthGrantTypes.AuthorizationCode }, options.Clients.Single().AllowedGrantTypes);
    }

    [TestMethod]
    public void AProgrammaticallyRegisteredClientWithNoGrantTypes_IsStillRejected()
    {
        // The default applies only on the configuration path, where an empty list cannot be told apart
        // from an omitted one. A caller registering a client in code — or through the administration
        // API, which turns this into a 400 — can genuinely say "no grant types", and silently handing
        // them authorization_code would grant a permission nobody asked for.
        var client = new OAuthClientRegistrationOptions
        {
            ClientId = "cli",
            ClientType = OAuthClientTypes.Public,
            AllowedGrantTypes = []
        };

        var error = Assert.ThrowsExactly<InvalidOperationException>(() => client.NormalizeAndValidate());

        StringAssert.Contains(error.Message, "at least one grant type");
    }

    private static OAuthAuthorizationServerOptions BindFromConfiguration(IDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var options = new OAuthAuthorizationServerOptions();
        configuration.Bind(options);
        return options;
    }

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
