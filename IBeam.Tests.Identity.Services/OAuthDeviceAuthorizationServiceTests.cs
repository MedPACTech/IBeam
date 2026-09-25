using System.Security.Claims;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Auth;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Identity.Services;

[TestClass]
public sealed class OAuthDeviceAuthorizationServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string Resource = "https://mcp.example.test";
    private const string VerificationUri = "https://app.example.test/device";

    [TestMethod]
    public async Task RequestAsync_IssuesHashedDeviceCodeAndReadableUserCode()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.RequestAsync(Request());

        Assert.AreNotEqual(result.DeviceCode, fixture.Devices.Created!.DeviceCodeHash);
        Assert.AreEqual(OAuthDeviceAuthorizationService.Hash(result.DeviceCode), fixture.Devices.Created.DeviceCodeHash);
        Assert.AreEqual('-', result.UserCode[4]);
        Assert.AreEqual(result.UserCode.Replace("-", ""), fixture.Devices.Created.UserCode);
        Assert.AreEqual(VerificationUri, result.VerificationUri);
        Assert.AreEqual($"{VerificationUri}?user_code={Uri.EscapeDataString(result.UserCode)}", result.VerificationUriComplete);
        Assert.AreEqual(15 * 60, result.ExpiresIn);
        Assert.AreEqual(5, result.Interval);
    }

    [TestMethod]
    public async Task RequestAsync_RejectsClientWithoutDeviceCodeGrant()
    {
        var fixture = CreateFixture(grantTypes: [OAuthGrantTypes.AuthorizationCode]);

        var ex = await Assert.ThrowsExactlyAsync<OAuthProtocolException>(() => fixture.Service.RequestAsync(Request()));

        Assert.AreEqual("unauthorized_client", ex.Error);
    }

    [TestMethod]
    public async Task RequestAsync_RejectsClientMissingDeviceVerificationUri()
    {
        var fixture = CreateFixture(deviceVerificationUri: null);

        var ex = await Assert.ThrowsExactlyAsync<OAuthProtocolException>(() => fixture.Service.RequestAsync(Request()));

        Assert.AreEqual("server_error", ex.Error);
    }

    [TestMethod]
    public async Task RequestAsync_RejectsUnknownScope()
    {
        var fixture = CreateFixture();

        var ex = await Assert.ThrowsExactlyAsync<OAuthProtocolException>(() =>
            fixture.Service.RequestAsync(Request() with { Scopes = ["tool:unknown"] }));

        Assert.AreEqual("invalid_scope", ex.Error);
    }

    [TestMethod]
    public async Task PrepareApprovalAsync_ReturnsNotFound_ForUnknownCode()
    {
        var fixture = CreateFixture();

        var context = await fixture.Service.PrepareApprovalAsync(Principal(), "NOPE-NOPE");

        Assert.IsFalse(context.Found);
        Assert.IsFalse(context.Usable);
    }

    [TestMethod]
    public async Task PrepareApprovalAsync_ReturnsClientNameAndScopes_ForPendingCode()
    {
        var fixture = CreateFixture();
        var started = await fixture.Service.RequestAsync(Request());

        var context = await fixture.Service.PrepareApprovalAsync(Principal(), started.UserCode);

        Assert.IsTrue(context.Found);
        Assert.IsTrue(context.Usable);
        Assert.AreEqual("Consumer CLI", context.ClientDisplayName);
        CollectionAssert.AreEquivalent(new[] { "tool:mcp" }, context.Scopes.ToArray());
    }

    [TestMethod]
    public async Task PrepareApprovalAsync_NormalizesUserCodeCaseAndDashes()
    {
        var fixture = CreateFixture();
        var started = await fixture.Service.RequestAsync(Request());
        var typedDifferently = started.UserCode.ToLowerInvariant().Replace("-", " ");

        var context = await fixture.Service.PrepareApprovalAsync(Principal(), typedDifferently);

        Assert.IsTrue(context.Usable);
    }

    [TestMethod]
    public async Task ApproveAsync_Denied_MarksDeniedWithoutGrantingScopes()
    {
        var fixture = CreateFixture();
        var started = await fixture.Service.RequestAsync(Request());

        var result = await fixture.Service.ApproveAsync(Principal(), new(started.UserCode, Approved: false));

        Assert.IsTrue(result.Success);
        Assert.IsTrue(fixture.Devices.Created!.IsDenied);
        Assert.IsNull(fixture.Devices.Created.UserId);
    }

    [TestMethod]
    public async Task ApproveAsync_Approved_RecordsUserTenantAndGrantedScopes()
    {
        var fixture = CreateFixture();
        var started = await fixture.Service.RequestAsync(Request());

        var result = await fixture.Service.ApproveAsync(Principal(), new(started.UserCode, Approved: true));

        Assert.IsTrue(result.Success);
        Assert.AreEqual(UserId, fixture.Devices.Created!.UserId);
        Assert.AreEqual(TenantId, fixture.Devices.Created.TenantId);
        CollectionAssert.AreEquivalent(new[] { "tool:mcp" }, fixture.Devices.Created.GrantedScopes!.ToArray());
    }

    [TestMethod]
    public async Task ApproveAsync_RejectsUnauthenticatedSubject()
    {
        var fixture = CreateFixture();
        var started = await fixture.Service.RequestAsync(Request());
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await fixture.Service.ApproveAsync(anonymous, new(started.UserCode, Approved: true));

        Assert.IsFalse(result.Success);
        Assert.AreEqual("access_denied", result.Error);
        Assert.IsTrue(fixture.Devices.Created!.IsPending);
    }

    [TestMethod]
    public async Task ApproveAsync_RejectsTenantMismatchForTenantScopedClient()
    {
        var fixture = CreateFixture(clientTenantId: Guid.NewGuid());
        var started = await fixture.Service.RequestAsync(Request());

        var result = await fixture.Service.ApproveAsync(Principal(), new(started.UserCode, Approved: true));

        Assert.IsFalse(result.Success);
        Assert.AreEqual("access_denied", result.Error);
    }

    [TestMethod]
    public async Task ApproveAsync_RejectsUnknownCode()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.ApproveAsync(Principal(), new("MADE-UPUP", Approved: true));

        Assert.IsFalse(result.Success);
        Assert.AreEqual("invalid_grant", result.Error);
    }

    [TestMethod]
    public async Task ApproveAsync_CannotBeReplayedOnceAlreadyDecided()
    {
        var fixture = CreateFixture();
        var started = await fixture.Service.RequestAsync(Request());
        await fixture.Service.ApproveAsync(Principal(), new(started.UserCode, Approved: true));

        var replay = await fixture.Service.ApproveAsync(Principal(), new(started.UserCode, Approved: true));

        Assert.IsFalse(replay.Success);
        Assert.AreEqual("invalid_grant", replay.Error);
    }

    private static Fixture CreateFixture(
        IReadOnlyList<string>? grantTypes = null,
        string? deviceVerificationUri = VerificationUri,
        Guid? clientTenantId = null)
    {
        var devices = new FakeDeviceStore();
        var client = new OAuthClientRecord(
            "cli-client", clientTenantId, "Consumer CLI", OAuthClientTypes.Public, [],
            grantTypes ?? [OAuthGrantTypes.DeviceCode], ["tool:mcp"], [Resource], true,
            OAuthClientStatuses.Active, null, null, DateTimeOffset.UtcNow,
            DeviceVerificationUri: deviceVerificationUri);
        return new(
            new OAuthDeviceAuthorizationService(
                new FakeClientStore(client),
                devices,
                new FakeConsentStore(),
                new AllowPermissionResolver(),
                Options.Create(new OAuthAuthorizationServerOptions
                {
                    Enabled = true,
                    Issuer = "https://identity.example.test",
                    DeviceCodeLifetimeMinutes = 15,
                    DeviceCodePollIntervalSeconds = 5
                })),
            devices);
    }

    private static OAuthDeviceAuthorizationRequest Request() => new("cli-client", ["tool:mcp"], Resource);

    private static ClaimsPrincipal Principal() => new(new ClaimsIdentity([
        new Claim("uid", UserId.ToString("D")),
        new Claim("tid", TenantId.ToString("D"))
    ], "test"));

    private sealed record Fixture(OAuthDeviceAuthorizationService Service, FakeDeviceStore Devices);

    private sealed class FakeClientStore(OAuthClientRecord client) : IOAuthClientStore
    {
        public Task<OAuthClientRecord?> GetAsync(string clientId, CancellationToken ct = default) => Task.FromResult<OAuthClientRecord?>(clientId == client.ClientId ? client : null);
        public Task<IReadOnlyList<OAuthClientRecord>> ListByTenantAsync(Guid? tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OAuthClientRecord> CreateAsync(OAuthClientRecord value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OAuthClientRecord> UpdateAsync(OAuthClientRecord value, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeConsentStore : IOAuthConsentStore
    {
        private OAuthConsentRecord? _consent;
        public Task<OAuthConsentRecord?> GetAsync(Guid userId, Guid tenantId, string clientId, string resource, CancellationToken ct = default) => Task.FromResult(_consent);
        public Task<OAuthConsentRecord> UpsertAsync(OAuthConsentRecord consent, CancellationToken ct = default) { _consent = consent; return Task.FromResult(consent); }
        public Task<bool> RevokeAsync(Guid userId, Guid tenantId, string clientId, string resource, DateTimeOffset revokedUtc, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class AllowPermissionResolver : IOAuthEffectivePermissionResolver
    {
        public Task<OAuthEffectivePermissionResult> ResolveAsync(OAuthPermissionResolutionRequest request, CancellationToken ct = default) =>
            Task.FromResult(new OAuthEffectivePermissionResult(request.RequestedScopes, [], []));
    }

    internal sealed class FakeDeviceStore : IOAuthDeviceAuthorizationStore
    {
        public OAuthDeviceAuthorizationRecord? Created { get; private set; }

        public Task<OAuthDeviceAuthorizationRecord> CreateAsync(OAuthDeviceAuthorizationRecord authorization, CancellationToken ct = default)
        {
            Created = authorization;
            return Task.FromResult(authorization);
        }

        public Task<OAuthDeviceAuthorizationRecord?> GetByDeviceCodeHashAsync(string deviceCodeHash, CancellationToken ct = default) =>
            Task.FromResult(Created?.DeviceCodeHash == deviceCodeHash ? Created : null);

        public Task<OAuthDeviceAuthorizationRecord?> GetByUserCodeAsync(string userCode, CancellationToken ct = default) =>
            Task.FromResult(Created?.UserCode == userCode ? Created : null);

        public Task<OAuthDeviceAuthorizationRecord?> TryApproveAsync(string userCode, Guid userId, Guid tenantId, IReadOnlyList<string> grantedScopes, DateTimeOffset approvedUtc, CancellationToken ct = default)
        {
            if (Created?.UserCode != userCode || !Created.IsPending) return Task.FromResult<OAuthDeviceAuthorizationRecord?>(null);
            Created = Created with { UserId = userId, TenantId = tenantId, GrantedScopes = grantedScopes, ApprovedUtc = approvedUtc };
            return Task.FromResult<OAuthDeviceAuthorizationRecord?>(Created);
        }

        public Task<OAuthDeviceAuthorizationRecord?> TryDenyAsync(string userCode, DateTimeOffset deniedUtc, CancellationToken ct = default)
        {
            if (Created?.UserCode != userCode || !Created.IsPending) return Task.FromResult<OAuthDeviceAuthorizationRecord?>(null);
            Created = Created with { DeniedUtc = deniedUtc };
            return Task.FromResult<OAuthDeviceAuthorizationRecord?>(Created);
        }

        public Task<OAuthDeviceAuthorizationRecord?> TryConsumeAsync(string deviceCodeHash, DateTimeOffset consumedUtc, CancellationToken ct = default)
        {
            if (Created?.DeviceCodeHash != deviceCodeHash || (!Created.IsDenied && !Created.IsUsable(consumedUtc)))
                return Task.FromResult<OAuthDeviceAuthorizationRecord?>(null);
            Created = Created with { ConsumedUtc = consumedUtc };
            return Task.FromResult<OAuthDeviceAuthorizationRecord?>(Created);
        }

        public Task<bool> RecordPollAsync(string deviceCodeHash, DateTimeOffset polledUtc, int minIntervalSeconds, CancellationToken ct = default)
        {
            if (Created?.DeviceCodeHash != deviceCodeHash) return Task.FromResult(false);
            var tooSoon = Created.LastPolledUtc is { } last && (polledUtc - last).TotalSeconds < minIntervalSeconds;
            Created = Created with { LastPolledUtc = polledUtc };
            return Task.FromResult(tooSoon);
        }
    }
}
