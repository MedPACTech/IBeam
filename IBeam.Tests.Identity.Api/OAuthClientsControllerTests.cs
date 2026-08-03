using System.Security.Claims;
using IBeam.Identity.Api.Controllers;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Identity.Api;

[TestClass]
public sealed class OAuthClientsControllerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [TestMethod]
    public async Task Create_ReturnsOkForTenantAdministrator()
    {
        var sut = CreateController(TenantId, [new("role", "Administrator")]);

        var result = await sut.Create(TenantId, new CreateOAuthClientRequest { DisplayName = "MCP" }, CancellationToken.None);

        Assert.IsInstanceOfType<OkObjectResult>(result);
    }

    [TestMethod]
    public async Task Create_ReturnsForbiddenForApiCredential()
    {
        var sut = CreateController(TenantId, [new("role", "Administrator"), new("api_subject_type", "credential")]);

        var result = await sut.Create(TenantId, new CreateOAuthClientRequest { DisplayName = "MCP" }, CancellationToken.None);

        Assert.AreEqual(StatusCodes.Status403Forbidden, ((ObjectResult)result).StatusCode);
    }

    [TestMethod]
    public async Task List_ReturnsForbiddenForDifferentTenant()
    {
        var sut = CreateController(TenantId, [new("role", "Administrator")]);

        var result = await sut.List(Guid.Parse("33333333-3333-3333-3333-333333333333"), CancellationToken.None);

        Assert.AreEqual(StatusCodes.Status403Forbidden, ((ObjectResult)result).StatusCode);
    }

    [TestMethod]
    public async Task Create_AllowsConfiguredOAuthClientPermission()
    {
        var options = new IBeamAccessControlOptions
        {
            OAuthClientManagementPermissionNames = ["identity.oauthclients.create"]
        };
        var sut = CreateController(TenantId, [new("permission", "identity.oauthclients.create")], options);

        var result = await sut.Create(TenantId, new CreateOAuthClientRequest { DisplayName = "MCP" }, CancellationToken.None);

        Assert.IsInstanceOfType<OkObjectResult>(result);
    }

    [TestMethod]
    public async Task Create_ReturnsBadRequestForInvalidRegistration()
    {
        var sut = CreateController(
            TenantId,
            [new("role", "Administrator")],
            service: new FakeService(throwOnCreate: true));

        var result = await sut.Create(TenantId, new CreateOAuthClientRequest(), CancellationToken.None);

        Assert.IsInstanceOfType<BadRequestObjectResult>(result);
    }

    [TestMethod]
    public async Task LifecycleEndpoints_ReturnSanitizedClientResults()
    {
        var sut = CreateController(TenantId, [new("role", "Administrator")]);

        var rotated = (OkObjectResult)await sut.RotateSecret(TenantId, "client", CancellationToken.None);
        var disabled = (OkObjectResult)await sut.Disable(TenantId, "client", CancellationToken.None);
        var revoked = (OkObjectResult)await sut.Revoke(TenantId, "client", CancellationToken.None);

        Assert.IsInstanceOfType<OAuthClientSecretRotatedResult>(rotated.Value);
        Assert.AreEqual(OAuthClientStatuses.Disabled, ((OAuthClientInfo)disabled.Value!).Status);
        Assert.AreEqual(OAuthClientStatuses.Revoked, ((OAuthClientInfo)revoked.Value!).Status);
        Assert.IsNull(typeof(OAuthClientInfo).GetProperty("ClientSecretHash"));
    }

    private static OAuthClientsController CreateController(
        Guid tenantId,
        IEnumerable<Claim> additionalClaims,
        IBeamAccessControlOptions? options = null,
        IOAuthClientAdministrationService? service = null)
    {
        var claims = new List<Claim>
        {
            new("tid", tenantId.ToString("D")),
            new("uid", UserId.ToString("D"))
        };
        claims.AddRange(additionalClaims);
        return new OAuthClientsController(
            service ?? new FakeService(),
            new StaticOptionsSnapshot<IBeamAccessControlOptions>(options ?? new IBeamAccessControlOptions()))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
                }
            }
        };
    }

    private sealed class FakeService(bool throwOnCreate = false) : IOAuthClientAdministrationService
    {
        public Task<OAuthClientCreatedResult> CreateAsync(Guid tenantId, CreateOAuthClientRequest request, CancellationToken ct = default)
        {
            if (throwOnCreate)
                throw new IdentityValidationException("Invalid OAuth client.");
            return Task.FromResult(new OAuthClientCreatedResult(Client(tenantId), "secret"));
        }
        public Task<IReadOnlyList<OAuthClientInfo>> ListAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OAuthClientInfo>>([Client(tenantId)]);
        public Task<OAuthClientInfo> GetAsync(Guid tenantId, string clientId, CancellationToken ct = default) => Task.FromResult(Client(tenantId));
        public Task<OAuthClientInfo> UpdateAsync(Guid tenantId, string clientId, UpdateOAuthClientRequest request, CancellationToken ct = default) => Task.FromResult(Client(tenantId));
        public Task<OAuthClientSecretRotatedResult> RotateSecretAsync(Guid tenantId, string clientId, CancellationToken ct = default) => Task.FromResult(new OAuthClientSecretRotatedResult(Client(tenantId), "rotated"));
        public Task<OAuthClientInfo> DisableAsync(Guid tenantId, string clientId, CancellationToken ct = default) => Task.FromResult(Client(tenantId) with { Status = OAuthClientStatuses.Disabled });
        public Task<OAuthClientInfo> RevokeAsync(Guid tenantId, string clientId, CancellationToken ct = default) => Task.FromResult(Client(tenantId) with { Status = OAuthClientStatuses.Revoked });

        private static OAuthClientInfo Client(Guid tenantId) => new(
            "client", tenantId, "MCP", OAuthClientTypes.Public, [], [OAuthGrantTypes.AuthorizationCode], [], [],
            true, OAuthClientStatuses.Active, DateTimeOffset.UtcNow, null, null, null, null, null);
    }

    private sealed class StaticOptionsSnapshot<T>(T value) : IOptionsSnapshot<T> where T : class
    {
        public T Value { get; } = value;
        public T Get(string? name) => Value;
    }
}
