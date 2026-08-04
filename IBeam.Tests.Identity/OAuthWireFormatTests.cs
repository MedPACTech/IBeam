using System.Text.Json;
using IBeam.Identity.Models;

namespace IBeam.Tests.Identity;

/// <summary>
/// These models are wire contracts defined by RFC, so what matters is the JSON member
/// names, not the C# property names. Asserting on the properties — as the controller
/// tests do — passes happily while the serialised document is unusable, which is how
/// camelCase metadata and a registration endpoint that ignored its request body both
/// shipped.
///
/// Serialised here with the ASP.NET web defaults, since that is what a host application
/// configures and what previously renamed every member.
/// </summary>
[TestClass]
public sealed class OAuthWireFormatTests
{
    private static readonly JsonSerializerOptions WebDefaults = new(JsonSerializerDefaults.Web);

    private static JsonElement Serialize<T>(T value)
        => JsonSerializer.SerializeToElement(value, WebDefaults);

    [TestMethod]
    public void AuthorizationServerMetadata_UsesTheNamesRfc8414Defines()
    {
        var metadata = new OAuthAuthorizationServerMetadata(
            "https://identity.example.test",
            "https://identity.example.test/oauth/authorize",
            "https://identity.example.test/oauth/token",
            "https://identity.example.test/oauth/revoke",
            "https://identity.example.test/.well-known/jwks.json",
            "https://identity.example.test/oauth/register",
            ["code"],
            [OAuthGrantTypes.AuthorizationCode],
            ["S256"],
            ["none"],
            ["tool:mcp"],
            true);

        var json = Serialize(metadata);

        foreach (var member in new[]
                 {
                     "issuer",
                     "authorization_endpoint",
                     "token_endpoint",
                     "revocation_endpoint",
                     "jwks_uri",
                     "registration_endpoint",
                     "response_types_supported",
                     "grant_types_supported",
                     "code_challenge_methods_supported",
                     "token_endpoint_auth_methods_supported",
                     "scopes_supported",
                     "resource_indicators_supported"
                 })
        {
            Assert.IsTrue(json.TryGetProperty(member, out _), $"missing RFC 8414 member: {member}");
        }

        // A client reading authorization_endpoint must not instead find authorizationEndpoint;
        // without it, discovery falls back to {issuer}/authorize and 404s.
        Assert.IsFalse(json.TryGetProperty("authorizationEndpoint", out _));
        Assert.AreEqual(
            "https://identity.example.test/oauth/authorize",
            json.GetProperty("authorization_endpoint").GetString());
    }

    [TestMethod]
    public void ClientMetadataDocument_UsesTheNamesRfc7591Defines()
    {
        var document = new OAuthClientMetadataDocument(
            "ibc_abc",
            "Claude",
            ["https://claude.ai/api/mcp/auth_callback"],
            [OAuthGrantTypes.AuthorizationCode],
            ["tool:mcp"],
            ["https://api.example.test/api/mcp"],
            "none");

        var json = Serialize(document);

        Assert.AreEqual("ibc_abc", json.GetProperty("client_id").GetString());
        Assert.AreEqual("Claude", json.GetProperty("client_name").GetString());
        Assert.AreEqual(
            "https://claude.ai/api/mcp/auth_callback",
            json.GetProperty("redirect_uris")[0].GetString());
        Assert.AreEqual("none", json.GetProperty("token_endpoint_auth_method").GetString());
        Assert.IsFalse(json.TryGetProperty("clientId", out _));
    }

    [TestMethod]
    public void RegistrationRequest_BindsTheNamesRfc7591ClientsSend()
    {
        // Exactly what a conformant client posts. Every field previously failed to bind,
        // producing a client with no redirect URIs that only failed later, at authorize.
        const string body = """
            {
              "client_name": "Claude",
              "redirect_uris": ["https://claude.ai/api/mcp/auth_callback"],
              "grant_types": ["authorization_code"],
              "scope": "tool:mcp api-scope:work",
              "resources": ["https://api.example.test/api/mcp"],
              "token_endpoint_auth_method": "none"
            }
            """;

        var request = JsonSerializer.Deserialize<DynamicOAuthClientRegistrationRequest>(body, WebDefaults);

        Assert.IsNotNull(request);
        Assert.AreEqual("Claude", request.ClientName);
        Assert.AreEqual("https://claude.ai/api/mcp/auth_callback", request.RedirectUris.Single());
        Assert.AreEqual(OAuthGrantTypes.AuthorizationCode, request.GrantTypes.Single());
        Assert.AreEqual("tool:mcp api-scope:work", request.Scope);
        Assert.AreEqual("https://api.example.test/api/mcp", request.Resources.Single());
        Assert.AreEqual("none", request.TokenEndpointAuthMethod);
    }

    [TestMethod]
    public void RegistrationRequest_WithNoRedirectUris_DoesNotSilentlyLookValid()
    {
        // Guards the failure mode rather than the fix: an empty body must not produce
        // something that looks like a usable client.
        var request = JsonSerializer.Deserialize<DynamicOAuthClientRegistrationRequest>("{}", WebDefaults);

        Assert.IsNotNull(request);
        Assert.AreEqual(0, request.RedirectUris.Count);
    }
}
