using System.Security.Claims;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Auth;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Identity.Services;

[TestClass]
public sealed class OAuthAuthorizationServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string RedirectUri = "https://app.example.test/callback";
    private const string Resource = "https://mcp.example.test";
    private const string Challenge = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_";

    [TestMethod]
    public async Task AuthorizeAsync_IssuesHashedBoundCodeAfterApproval()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.AuthorizeAsync(Principal(), new(Request(), true));

        Assert.IsNotNull(result.Code);
        Assert.AreNotEqual(result.Code, fixture.Codes.Created!.CodeHash);
        Assert.AreEqual(OAuthAuthorizationService.Hash(result.Code), fixture.Codes.Created.CodeHash);
        Assert.AreEqual(Challenge, fixture.Codes.Created.CodeChallenge);
        Assert.AreEqual(Resource, fixture.Codes.Created.Resource);
        Assert.AreEqual(UserId, fixture.Codes.Created.UserId);
    }

    [TestMethod]
    public async Task PrepareAsync_RejectsRedirectMismatchWithoutTrustedRedirect()
    {
        var ex = await Assert.ThrowsExactlyAsync<OAuthProtocolException>(() =>
            CreateFixture().Service.PrepareAsync(Principal(), Request() with { RedirectUri = "https://evil.example/callback" }));

        Assert.AreEqual("invalid_request", ex.Error);
        Assert.IsNull(ex.RedirectUri);
    }

    [TestMethod]
    public async Task PrepareAsync_RejectsInvalidResource()
    {
        var ex = await Assert.ThrowsExactlyAsync<OAuthProtocolException>(() =>
            CreateFixture().Service.PrepareAsync(Principal(), Request() with { Resource = "https://other.example" }));

        Assert.AreEqual("invalid_target", ex.Error);
        Assert.AreEqual(RedirectUri, ex.RedirectUri);
    }

    [TestMethod]
    public async Task PrepareAsync_RejectsMissingPkce()
    {
        var ex = await Assert.ThrowsExactlyAsync<OAuthProtocolException>(() =>
            CreateFixture().Service.PrepareAsync(Principal(), Request() with { CodeChallenge = string.Empty }));

        Assert.AreEqual("invalid_request", ex.Error);
    }

    [TestMethod]
    public async Task AuthorizeAsync_ReturnsAccessDeniedWithoutCreatingCode()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.AuthorizeAsync(Principal(), new(Request(), false));

        Assert.AreEqual("access_denied", result.Error);
        Assert.IsNull(fixture.Codes.Created);
    }

    [TestMethod]
    public async Task PrepareAsync_RejectsTenantMismatch()
    {
        var ex = await Assert.ThrowsExactlyAsync<OAuthProtocolException>(() =>
            CreateFixture().Service.PrepareAsync(Principal(), Request() with { TenantId = Guid.NewGuid() }));

        Assert.AreEqual("access_denied", ex.Error);
    }

    [TestMethod]
    public async Task AuthorizationCodeStore_AllowsOnlyOneConsumption()
    {
        var fixture = CreateFixture();
        var result = await fixture.Service.AuthorizeAsync(Principal(), new(Request(), true));
        var hash = OAuthAuthorizationService.Hash(result.Code!);

        var first = await fixture.Codes.TryConsumeAsync(hash, DateTimeOffset.UtcNow);
        var replay = await fixture.Codes.TryConsumeAsync(hash, DateTimeOffset.UtcNow);

        Assert.IsNotNull(first);
        Assert.IsNull(replay);
    }

    private static Fixture CreateFixture()
    {
        var codes = new FakeCodeStore();
        var client = new OAuthClientRecord(
            "client", TenantId, "Consumer", OAuthClientTypes.Public, [RedirectUri],
            [OAuthGrantTypes.AuthorizationCode], ["tool:mcp"], [Resource], true,
            OAuthClientStatuses.Active, null, null, DateTimeOffset.UtcNow);
        return new(
            new OAuthAuthorizationService(
                new FakeClientStore(client),
                new FakeConsentStore(),
                codes,
                new AllowPermissionResolver(),
                Options.Create(new OAuthAuthorizationServerOptions
                {
                    Enabled = true,
                    Issuer = "https://identity.example.test"
                })),
            codes);
    }

    private static OAuthAuthorizationRequest Request() => new(
        "code", "client", RedirectUri, "state", ["tool:mcp"], Resource,
        Challenge, OAuthCodeChallengeMethods.S256, TenantId);

    private static ClaimsPrincipal Principal() => new(new ClaimsIdentity([
        new Claim("uid", UserId.ToString("D")),
        new Claim("tid", TenantId.ToString("D")),
        new Claim("role", "tool:mcp")
    ], "test"));

    private sealed record Fixture(OAuthAuthorizationService Service, FakeCodeStore Codes);

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

    private sealed class FakeCodeStore : IOAuthAuthorizationCodeStore
    {
        public OAuthAuthorizationCodeRecord? Created { get; private set; }
        public Task<OAuthAuthorizationCodeRecord> CreateAsync(OAuthAuthorizationCodeRecord code, CancellationToken ct = default) { Created = code; return Task.FromResult(code); }
        public Task<OAuthAuthorizationCodeRecord?> GetByHashAsync(string codeHash, CancellationToken ct = default) => Task.FromResult(Created?.CodeHash == codeHash ? Created : null);
        public Task<OAuthAuthorizationCodeRecord?> TryConsumeAsync(string codeHash, DateTimeOffset consumedUtc, CancellationToken ct = default)
        {
            if (Created?.CodeHash != codeHash || Created.ConsumedUtc is not null) return Task.FromResult<OAuthAuthorizationCodeRecord?>(null);
            Created = Created with { ConsumedUtc = consumedUtc };
            return Task.FromResult<OAuthAuthorizationCodeRecord?>(Created);
        }
    }

    private sealed class AllowPermissionResolver : IOAuthEffectivePermissionResolver
    {
        public Task<OAuthEffectivePermissionResult> ResolveAsync(OAuthPermissionResolutionRequest request, CancellationToken ct = default) =>
            Task.FromResult(new OAuthEffectivePermissionResult(request.RequestedScopes, [], []));
    }
}
