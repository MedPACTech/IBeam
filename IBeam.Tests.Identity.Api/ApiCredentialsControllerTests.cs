using System.Security.Claims;
using IBeam.Identity.Api.Controllers;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.Tests.Identity.Api;

[TestClass]
public sealed class ApiCredentialsControllerTests
{
    private static readonly Guid TenantId = Guid.Parse("225925cc-995e-4584-a63b-4f2cb4f38f6f");
    private static readonly Guid UserId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [TestMethod]
    public async Task Create_ReturnsForbidden_ForCredentialSubject()
    {
        var sut = CreateController([
            new Claim("tid", TenantId.ToString("D")),
            new Claim("uid", Guid.NewGuid().ToString("D")),
            new Claim("role", "admin"),
            new Claim("api_subject_type", "credential")
        ]);

        var result = await sut.Create(new CreateApiCredentialRequest { DisplayName = "Worker" }, CancellationToken.None);

        Assert.IsInstanceOfType<ObjectResult>(result);
        Assert.AreEqual(StatusCodes.Status403Forbidden, ((ObjectResult)result).StatusCode);
    }

    [TestMethod]
    public async Task Create_ReturnsOk_ForHumanTenantAdmin()
    {
        var sut = CreateController([
            new Claim("tid", TenantId.ToString("D")),
            new Claim("uid", UserId.ToString("D")),
            new Claim("role", "admin")
        ]);

        var result = await sut.Create(new CreateApiCredentialRequest { DisplayName = "Worker" }, CancellationToken.None);

        Assert.IsInstanceOfType<OkObjectResult>(result);
    }

    private static ApiCredentialsController CreateController(IEnumerable<Claim> claims)
    {
        var controller = new ApiCredentialsController(new FakeApiCredentialService(), new FakeApiCredentialAuthenticator())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "unit-test"))
                }
            }
        };

        return controller;
    }

    private sealed class FakeApiCredentialService : IApiCredentialService
    {
        public Task<CreateApiCredentialResult> CreateAsync(
            Guid tenantId,
            CreateApiCredentialRequest request,
            Guid? createdByUserId,
            CancellationToken ct = default)
            => Task.FromResult(new CreateApiCredentialResult
            {
                ApiKey = "ibk_raw",
                Credential = new ApiCredentialInfo(
                    Id: Guid.NewGuid(),
                    TenantId: tenantId,
                    DisplayName: request.DisplayName,
                    AgentKey: request.AgentKey,
                    RoleNames: request.RoleNames,
                    RoleIds: request.RoleIds,
                    KeyPrefix: "ibk_12345678",
                    CreatedUtc: DateTimeOffset.UtcNow,
                    CreatedByUserId: createdByUserId,
                    ExpiresUtc: null,
                    LastUsedUtc: null,
                    LastUsedIp: null,
                    RevokedUtc: null,
                    RevokedByUserId: null,
                    RevocationReason: null,
                    IsDeleted: false)
            });

        public Task<IReadOnlyList<ApiCredentialInfo>> ListAsync(Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ApiCredentialInfo>>(Array.Empty<ApiCredentialInfo>());

        public Task<ApiCredentialInfo> UpdateRolesAsync(
            Guid tenantId,
            Guid credentialId,
            UpdateApiCredentialRolesRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ApiCredentialInfo> RevokeAsync(
            Guid tenantId,
            Guid credentialId,
            Guid? revokedByUserId,
            string? reason,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeApiCredentialAuthenticator : IApiCredentialAuthenticator
    {
        public Task<ApiCredentialAuthenticationResult> AuthenticateAsync(string apiKey, string? ipAddress = null, CancellationToken ct = default)
            => Task.FromResult(ApiCredentialAuthenticationResult.Fail("not_implemented"));
    }
}
