using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Auth;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Identity.Services;

[TestClass]
public sealed class OAuthClientAdministrationServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [TestMethod]
    public async Task CreateAsync_ReturnsConfidentialSecretOnceWithoutHashInDto()
    {
        var sut = CreateService();

        var created = await sut.CreateAsync(TenantId, ConfidentialRequest());
        var fetched = await sut.GetAsync(TenantId, created.Client.ClientId);

        Assert.IsNotNull(created.ClientSecret);
        Assert.IsGreaterThanOrEqualTo(32, created.ClientSecret.Length);
        Assert.AreEqual(created.Client, fetched);
        Assert.IsNull(typeof(OAuthClientInfo).GetProperty("ClientSecretHash"));
        Assert.IsNull(typeof(OAuthClientInfo).GetProperty("ClientSecret"));
    }

    [TestMethod]
    public async Task CreateAsync_RejectsPublicClientWithoutPkce()
    {
        var request = new CreateOAuthClientRequest
        {
            DisplayName = "Browser",
            ClientType = OAuthClientTypes.Public,
            RedirectUris = ["https://app.example.test/callback"],
            AllowedGrantTypes = [OAuthGrantTypes.AuthorizationCode],
            AllowedScopes = ["tool:mcp"],
            AllowedResources = ["https://mcp.example.test"],
            RequirePkce = false
        };

        await Assert.ThrowsExactlyAsync<IdentityValidationException>(() => CreateService().CreateAsync(TenantId, request));
    }

    [TestMethod]
    public async Task RotateSecretAsync_ReturnsNewSecretAndUpdatesRotationTime()
    {
        var sut = CreateService();
        var created = await sut.CreateAsync(TenantId, ConfidentialRequest());

        var rotated = await sut.RotateSecretAsync(TenantId, created.Client.ClientId);

        Assert.AreNotEqual(created.ClientSecret, rotated.ClientSecret);
        Assert.IsNotNull(rotated.Client.SecretRotatedUtc);
    }

    [TestMethod]
    public async Task DisableAndRevokeAsync_ApplyTerminalLifecycleStates()
    {
        var sut = CreateService();
        var created = await sut.CreateAsync(TenantId, ConfidentialRequest());

        var disabled = await sut.DisableAsync(TenantId, created.Client.ClientId);
        var revoked = await sut.RevokeAsync(TenantId, created.Client.ClientId);

        Assert.AreEqual(OAuthClientStatuses.Disabled, disabled.Status);
        Assert.IsNotNull(disabled.DisabledUtc);
        Assert.AreEqual(OAuthClientStatuses.Revoked, revoked.Status);
        Assert.IsNotNull(revoked.RevokedUtc);
        await Assert.ThrowsExactlyAsync<IdentityValidationException>(
            () => sut.RotateSecretAsync(TenantId, created.Client.ClientId));
    }

    [TestMethod]
    public async Task GetAsync_HidesClientFromDifferentTenant()
    {
        var sut = CreateService();
        var created = await sut.CreateAsync(TenantId, ConfidentialRequest());

        await Assert.ThrowsExactlyAsync<IdentityNotFoundException>(
            () => sut.GetAsync(Guid.Parse("22222222-2222-2222-2222-222222222222"), created.Client.ClientId));
    }

    private static OAuthClientAdministrationService CreateService() => new(
        new InMemoryOAuthClientStore(Options.Create(new OAuthAuthorizationServerOptions())),
        new FakeSecretHasher());

    private static CreateOAuthClientRequest ConfidentialRequest() => new()
    {
        DisplayName = "Worker",
        ClientType = OAuthClientTypes.Confidential,
        AllowedGrantTypes = [OAuthGrantTypes.ClientCredentials],
        AllowedScopes = ["tool:mcp", "api-scope:work"],
        AllowedResources = ["https://mcp.example.test"],
        RequirePkce = false
    };

    private sealed class FakeSecretHasher : IApiCredentialSecretHasher
    {
        public string Hash(string secret) => $"hash:{secret}";
        public bool Verify(string secret, string storedHash) => storedHash == Hash(secret);
    }
}
