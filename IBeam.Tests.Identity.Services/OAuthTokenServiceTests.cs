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

    [TestMethod]
    public async Task DeviceCode_Pending_ReturnsAuthorizationPendingWithoutConsuming()
    {
        var client = Client(OAuthGrantTypes.DeviceCode);
        var record = DeviceRecord(client.ClientId);
        var devices = new Mock<IOAuthDeviceAuthorizationStore>();
        devices.Setup(x => x.GetByDeviceCodeHashAsync(record.DeviceCodeHash, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        devices.Setup(x => x.RecordPollAsync(record.DeviceCodeHash, It.IsAny<DateTimeOffset>(), record.IntervalSeconds, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var sut = CreateService(client, devices: devices);

        var ex = await Assert.ThrowsExactlyAsync<OAuthProtocolException>(() => sut.ExchangeAsync(new(
            OAuthGrantTypes.DeviceCode, client.ClientId, "secret", DeviceCode: "raw-device-code")));

        Assert.AreEqual(OAuthDeviceErrors.AuthorizationPending, ex.Error);
        devices.Verify(x => x.TryConsumeAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task DeviceCode_PolledTooSoon_ReturnsSlowDown()
    {
        var client = Client(OAuthGrantTypes.DeviceCode);
        var record = DeviceRecord(client.ClientId);
        var devices = new Mock<IOAuthDeviceAuthorizationStore>();
        devices.Setup(x => x.GetByDeviceCodeHashAsync(record.DeviceCodeHash, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        devices.Setup(x => x.RecordPollAsync(record.DeviceCodeHash, It.IsAny<DateTimeOffset>(), record.IntervalSeconds, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var sut = CreateService(client, devices: devices);

        var ex = await Assert.ThrowsExactlyAsync<OAuthProtocolException>(() => sut.ExchangeAsync(new(
            OAuthGrantTypes.DeviceCode, client.ClientId, "secret", DeviceCode: "raw-device-code")));

        Assert.AreEqual(OAuthDeviceErrors.SlowDown, ex.Error);
    }

    [TestMethod]
    public async Task DeviceCode_Expired_ReturnsExpiredToken()
    {
        var client = Client(OAuthGrantTypes.DeviceCode);
        var record = DeviceRecord(client.ClientId) with { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1) };
        var devices = new Mock<IOAuthDeviceAuthorizationStore>();
        devices.Setup(x => x.GetByDeviceCodeHashAsync(record.DeviceCodeHash, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        var sut = CreateService(client, devices: devices);

        var ex = await Assert.ThrowsExactlyAsync<OAuthProtocolException>(() => sut.ExchangeAsync(new(
            OAuthGrantTypes.DeviceCode, client.ClientId, "secret", DeviceCode: "raw-device-code")));

        Assert.AreEqual(OAuthDeviceErrors.ExpiredToken, ex.Error);
    }

    [TestMethod]
    public async Task DeviceCode_Denied_ReturnsAccessDeniedAndConsumes()
    {
        var client = Client(OAuthGrantTypes.DeviceCode);
        var record = DeviceRecord(client.ClientId) with { DeniedUtc = DateTimeOffset.UtcNow };
        var devices = new Mock<IOAuthDeviceAuthorizationStore>();
        devices.Setup(x => x.GetByDeviceCodeHashAsync(record.DeviceCodeHash, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        devices.Setup(x => x.TryConsumeAsync(record.DeviceCodeHash, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(record with { ConsumedUtc = DateTimeOffset.UtcNow });
        var sut = CreateService(client, devices: devices);

        var ex = await Assert.ThrowsExactlyAsync<OAuthProtocolException>(() => sut.ExchangeAsync(new(
            OAuthGrantTypes.DeviceCode, client.ClientId, "secret", DeviceCode: "raw-device-code")));

        Assert.AreEqual("access_denied", ex.Error);
    }

    [TestMethod]
    public async Task DeviceCode_Approved_ConsumesAndIssuesTokenBoundToGrantedScopes()
    {
        var client = Client(OAuthGrantTypes.DeviceCode);
        var userId = Guid.NewGuid();
        var record = DeviceRecord(client.ClientId) with
        {
            UserId = userId,
            TenantId = TenantId,
            GrantedScopes = ["tool:mcp"],
            ApprovedUtc = DateTimeOffset.UtcNow
        };
        var devices = new Mock<IOAuthDeviceAuthorizationStore>();
        devices.Setup(x => x.GetByDeviceCodeHashAsync(record.DeviceCodeHash, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        devices.Setup(x => x.TryConsumeAsync(record.DeviceCodeHash, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(record with { ConsumedUtc = DateTimeOffset.UtcNow });
        var sut = CreateService(client, devices: devices);

        var result = await sut.ExchangeAsync(new(OAuthGrantTypes.DeviceCode, client.ClientId, "secret", DeviceCode: "raw-device-code"));
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken);

        Assert.AreEqual(userId.ToString("D"), jwt.Claims.Single(x => x.Type == "sub").Value);
        Assert.AreEqual(TenantId.ToString("D"), jwt.Claims.Single(x => x.Type == "tid").Value);
        CollectionAssert.Contains(jwt.Audiences.ToList(), Resource);
    }

    [TestMethod]
    public async Task DeviceCode_UnknownCode_ReturnsInvalidGrant()
    {
        var client = Client(OAuthGrantTypes.DeviceCode);
        var devices = new Mock<IOAuthDeviceAuthorizationStore>();
        devices.Setup(x => x.GetByDeviceCodeHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((OAuthDeviceAuthorizationRecord?)null);
        var sut = CreateService(client, devices: devices);

        var ex = await Assert.ThrowsExactlyAsync<OAuthProtocolException>(() => sut.ExchangeAsync(new(
            OAuthGrantTypes.DeviceCode, client.ClientId, "secret", DeviceCode: "raw-device-code")));

        Assert.AreEqual("invalid_grant", ex.Error);
    }

    private static OAuthDeviceAuthorizationRecord DeviceRecord(string clientId) => new(
        OAuthDeviceAuthorizationService.Hash("raw-device-code"), "WDJBMJHT", clientId, ["tool:mcp"], Resource,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(15), 5);

    private static OAuthTokenService CreateService(
        OAuthClientRecord client,
        Mock<IOAuthAuthorizationCodeStore>? codes = null,
        Mock<IAuthSessionStore>? sessions = null,
        Mock<IOAuthDeviceAuthorizationStore>? devices = null)
    {
        var clients = new Mock<IOAuthClientStore>();
        clients.Setup(x => x.GetAsync(client.ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(client);
        var hasher = new Mock<IApiCredentialSecretHasher>();
        hasher.Setup(x => x.Verify("secret", "hash")).Returns(true);
        var jwt = new JwtOptions { Issuer = "issuer", Audience = "default", SigningKey = "test-signing-key-with-enough-length-1234567890" };
        return new(
            clients.Object,
            (codes ?? new Mock<IOAuthAuthorizationCodeStore>()).Object,
            (devices ?? new Mock<IOAuthDeviceAuthorizationStore>()).Object,
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
