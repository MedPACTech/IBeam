using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Auth;
using IBeam.Identity.Services.Tokens;
using Microsoft.Extensions.Options;
using Moq;

namespace IBeam.Tests.Identity.Services;

[TestClass]
public sealed class OAuthTokenServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string Resource = "https://mcp.example.test";

    [TestMethod]
    public async Task ClientCredentials_IssuesResourceBoundMachineTokenWithoutRefreshToken()
    {
        var client = Client(OAuthGrantTypes.ClientCredentials);
        var sut = CreateService(client);

        var result = await sut.ExchangeAsync(new(
            OAuthGrantTypes.ClientCredentials, client.ClientId, "secret", Resource: Resource,
            Scopes: ["tool:mcp"]));
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken);

        CollectionAssert.Contains(jwt.Audiences.ToList(), Resource);
        Assert.AreEqual(client.ClientId, jwt.Claims.Single(x => x.Type == "client_id").Value);
        Assert.AreEqual("mcp", jwt.Claims.Single(x => x.Type == "tool").Value);
        Assert.IsNull(result.RefreshToken);
    }

    [TestMethod]
    public async Task AuthorizationCode_RejectsPkceMismatchWithoutConsumingCode()
    {
        var client = Client(OAuthGrantTypes.AuthorizationCode);
        var code = new OAuthAuthorizationCodeRecord(
            OAuthAuthorizationService.Hash("code"), client.ClientId, "https://app.example/callback",
            Guid.NewGuid(), TenantId, ["tool:mcp"], Resource, Challenge("correct"),
            OAuthCodeChallengeMethods.S256, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5));
        var codes = new Mock<IOAuthAuthorizationCodeStore>();
        codes.Setup(x => x.GetByHashAsync(code.CodeHash, It.IsAny<CancellationToken>())).ReturnsAsync(code);
        var sut = CreateService(client, codes);

        var ex = await Assert.ThrowsExactlyAsync<OAuthProtocolException>(() => sut.ExchangeAsync(new(
            OAuthGrantTypes.AuthorizationCode, client.ClientId, "secret", "code",
            code.RedirectUri, "wrong", Resource: Resource)));

        Assert.AreEqual("invalid_grant", ex.Error);
        codes.Verify(x => x.TryConsumeAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task AuthorizationCode_ConsumesCodeAndIssuesLeastPrivilegeAudience()
    {
        var client = Client(OAuthGrantTypes.AuthorizationCode);
        var code = new OAuthAuthorizationCodeRecord(
            OAuthAuthorizationService.Hash("code"), client.ClientId, "https://app.example/callback",
            Guid.NewGuid(), TenantId, ["tool:mcp"], Resource, Challenge("correct"),
            OAuthCodeChallengeMethods.S256, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5));
        var codes = new Mock<IOAuthAuthorizationCodeStore>();
        codes.Setup(x => x.GetByHashAsync(code.CodeHash, It.IsAny<CancellationToken>())).ReturnsAsync(code);
        codes.Setup(x => x.TryConsumeAsync(code.CodeHash, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(code with { ConsumedUtc = DateTimeOffset.UtcNow });

        var result = await CreateService(client, codes).ExchangeAsync(new(
            OAuthGrantTypes.AuthorizationCode, client.ClientId, "secret", "code",
            code.RedirectUri, "correct", Resource: Resource));
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken);

        CollectionAssert.Contains(jwt.Audiences.ToList(), Resource);
        CollectionAssert.AreEquivalent(new[] { "tool:mcp" }, result.Scope.Split(' '));
        codes.Verify(x => x.TryConsumeAsync(code.CodeHash, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task RefreshToken_RotatesAndRejectsReusedToken()
    {
        var client = Client(OAuthGrantTypes.RefreshToken);
        var sessions = new Mock<IAuthSessionStore>();
        var claims = System.Text.Json.JsonSerializer.Serialize(new List<ClaimItem>
        {
            new("sub", Guid.NewGuid().ToString("D")), new("tid", TenantId.ToString("D")),
            new("client_id", client.ClientId), new("resource", Resource), new("role", "tool:mcp"), new("tool", "mcp")
        });
        var session = new AuthSessionRecord(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("refresh"))).ToLowerInvariant(),
            "session", Guid.NewGuid(), TenantId, claims, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(1));
        sessions.SetupSequence(x => x.GetByRefreshTokenHashAsync(session.RefreshTokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session)
            .ReturnsAsync((AuthSessionRecord?)null);
        sessions.Setup(x => x.DeleteByRefreshTokenHashAsync(session.RefreshTokenHash, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        sessions.Setup(x => x.SaveAsync(It.IsAny<AuthSessionRecord>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var sut = CreateService(client, sessions: sessions);

        var rotated = await sut.ExchangeAsync(new(OAuthGrantTypes.RefreshToken, client.ClientId, "secret", RefreshToken: "refresh"));
        var replay = await Assert.ThrowsExactlyAsync<OAuthProtocolException>(() =>
            sut.ExchangeAsync(new(OAuthGrantTypes.RefreshToken, client.ClientId, "secret", RefreshToken: "refresh")));

        Assert.IsNotNull(rotated.RefreshToken);
        Assert.AreNotEqual("refresh", rotated.RefreshToken);
        Assert.AreEqual("invalid_grant", replay.Error);
    }

    [TestMethod]
    public async Task DisabledClient_IsRejectedBeforeGrantProcessing()
    {
        var client = Client(OAuthGrantTypes.ClientCredentials) with
        {
            Status = OAuthClientStatuses.Disabled,
            DisabledUtc = DateTimeOffset.UtcNow
        };

        var ex = await Assert.ThrowsExactlyAsync<OAuthProtocolException>(() => CreateService(client).ExchangeAsync(new(
            OAuthGrantTypes.ClientCredentials, client.ClientId, "secret", Resource: Resource, Scopes: ["tool:mcp"])));

        Assert.AreEqual("invalid_client", ex.Error);
    }

    private static OAuthTokenService CreateService(
        OAuthClientRecord client,
        Mock<IOAuthAuthorizationCodeStore>? codes = null,
        Mock<IAuthSessionStore>? sessions = null)
    {
        var clients = new Mock<IOAuthClientStore>();
        clients.Setup(x => x.GetAsync(client.ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(client);
        var hasher = new Mock<IApiCredentialSecretHasher>();
        hasher.Setup(x => x.Verify("secret", "hash")).Returns(true);
        var jwt = new JwtOptions { Issuer = "issuer", Audience = "default", SigningKey = "test-signing-key-with-enough-length-1234567890" };
        return new(
            clients.Object,
            (codes ?? new Mock<IOAuthAuthorizationCodeStore>()).Object,
            new Mock<IOAuthConsentStore>().Object,
            (sessions ?? new Mock<IAuthSessionStore>()).Object,
            hasher.Object,
            new JwtSigningKeyProvider(Options.Create(jwt)),
            Options.Create(jwt));
    }

    private static OAuthClientRecord Client(string grant) => new(
        "client", TenantId, "Machine", OAuthClientTypes.Confidential, [], [grant], ["tool:mcp"],
        [Resource], false, OAuthClientStatuses.Active, "hash", "pbkdf2-sha256", DateTimeOffset.UtcNow);

    private static string Challenge(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
