using System.Security.Claims;
using IBeam.Identity.Api.Controllers;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

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

    [TestMethod]
    public async Task Create_ReturnsOk_ForMappedTenantClaimAndAdministratorRole()
    {
        var sut = CreateController([
            new Claim("http://schemas.microsoft.com/identity/claims/tenantid", TenantId.ToString("D")),
            new Claim(ClaimTypes.NameIdentifier, UserId.ToString("D")),
            new Claim(ClaimTypes.Role, "Administrator")
        ]);

        var result = await sut.Create(new CreateApiCredentialRequest { DisplayName = "Worker" }, CancellationToken.None);

        Assert.IsInstanceOfType<OkObjectResult>(result);
    }

    [TestMethod]
    public async Task Create_ReturnsOk_ForTenantIdAliasAndOwnerRolesClaim()
    {
        var sut = CreateController([
            new Claim("tenant_id", TenantId.ToString("D")),
            new Claim("sub", UserId.ToString("D")),
            new Claim("roles", "Owner")
        ]);

        var result = await sut.Create(new CreateApiCredentialRequest { DisplayName = "Worker" }, CancellationToken.None);

        Assert.IsInstanceOfType<OkObjectResult>(result);
    }

    [TestMethod]
    public async Task Create_ReturnsOk_ForJwtArrayRoleClaimShape()
    {
        var sut = CreateController([
            new Claim("tid", TenantId.ToString("D")),
            new Claim("uid", UserId.ToString("D")),
            new Claim("role", """["Administrator","Owner"]""")
        ]);

        var result = await sut.Create(new CreateApiCredentialRequest { DisplayName = "Worker" }, CancellationToken.None);

        Assert.IsInstanceOfType<OkObjectResult>(result);
    }

    [TestMethod]
    public async Task Create_ReturnsOk_ForConfiguredApiCredentialPermission()
    {
        var sut = CreateController(
        [
            new Claim("tid", TenantId.ToString("D")),
            new Claim("uid", UserId.ToString("D")),
            new Claim("role", "member"),
            new Claim("permission", "identity.apikeys.manage")
        ],
        new IBeamAccessControlOptions
        {
            ApiCredentialManagementPermissionNames = ["identity.apikeys.manage"]
        });

        var result = await sut.Create(new CreateApiCredentialRequest { DisplayName = "Worker" }, CancellationToken.None);

        Assert.IsInstanceOfType<OkObjectResult>(result);
    }

    [TestMethod]
    public async Task RoleCatalog_ReturnsBuiltInApiCredentialScopes_ForHumanTenantAdmin()
    {
        var sut = CreateController([
            new Claim("tid", TenantId.ToString("D")),
            new Claim("uid", UserId.ToString("D")),
            new Claim("role", "admin")
        ]);

        var result = await sut.RoleCatalog(CancellationToken.None);

        Assert.IsInstanceOfType<OkObjectResult>(result);
        var entries = (IReadOnlyList<ApiCredentialRoleCatalogEntry>)((OkObjectResult)result).Value!;
        CollectionAssert.Contains(entries.Select(x => x.Name).ToList(), "API");
        CollectionAssert.Contains(entries.Select(x => x.Name).ToList(), "tool:mcp");
        CollectionAssert.Contains(entries.Select(x => x.Name).ToList(), "api-scope:work");
    }

    [TestMethod]
    public async Task RoleCatalog_ReturnsForbidden_ForCredentialSubject()
    {
        var sut = CreateController([
            new Claim("tid", TenantId.ToString("D")),
            new Claim("uid", UserId.ToString("D")),
            new Claim("role", "admin"),
            new Claim("api_subject_type", "credential")
        ]);

        var result = await sut.RoleCatalog(CancellationToken.None);

        Assert.IsInstanceOfType<ObjectResult>(result);
        Assert.AreEqual(StatusCodes.Status403Forbidden, ((ObjectResult)result).StatusCode);
    }

    [TestMethod]
    public async Task ScopeCatalog_ReturnsScopes_ForHumanTenantAdmin()
    {
        var sut = CreateController([
            new Claim("tid", TenantId.ToString("D")),
            new Claim("uid", UserId.ToString("D")),
            new Claim("role", "admin")
        ]);

        var result = await sut.ScopeCatalog(TenantId, CancellationToken.None);

        Assert.IsInstanceOfType<OkObjectResult>(result);
        var entries = (IReadOnlyList<ApiScopeCatalogItem>)((OkObjectResult)result).Value!;
        CollectionAssert.Contains(entries.Select(x => x.Key).ToList(), "work");
        CollectionAssert.Contains(entries.Select(x => x.Key).ToList(), "mcp");
    }

    private static ApiCredentialsController CreateController(
        IEnumerable<Claim> claims,
        IBeamAccessControlOptions? accessOptions = null)
    {
        var controller = new ApiCredentialsController(
            new FakeApiCredentialService(),
            new FakeApiCredentialAuthenticator(),
            new FakeApiCredentialRoleCatalogProvider(),
            new FakeApiCredentialScopeCatalogProvider(),
            new FakeApiCredentialAccessService(),
            new StaticOptionsSnapshot<IBeamAccessControlOptions>(accessOptions ?? new IBeamAccessControlOptions()))
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

    private sealed class StaticOptionsSnapshot<T> : IOptionsSnapshot<T>
        where T : class
    {
        public StaticOptionsSnapshot(T value) => Value = value;
        public T Value { get; }
        public T Get(string? name) => Value;
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
                    RotatedUtc: null,
                    RevokedUtc: null,
                    RevokedByUserId: null,
                    RevocationReason: null,
                    IsDeleted: false)
            });

        public Task<IReadOnlyList<ApiCredentialInfo>> ListAsync(Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ApiCredentialInfo>>(Array.Empty<ApiCredentialInfo>());

        public Task<ApiCredentialInfo> GetAsync(Guid tenantId, Guid credentialId, CancellationToken ct = default)
            => Task.FromResult(new ApiCredentialInfo(
                Id: credentialId,
                TenantId: tenantId,
                DisplayName: "Worker",
                AgentKey: "codex",
                RoleNames: ["API", "api-scope:work", "tool:mcp"],
                RoleIds: [],
                KeyPrefix: "ibk_12345678",
                CreatedUtc: DateTimeOffset.UtcNow,
                CreatedByUserId: UserId,
                ExpiresUtc: null,
                LastUsedUtc: null,
                LastUsedIp: null,
                RotatedUtc: null,
                RevokedUtc: null,
                RevokedByUserId: null,
                RevocationReason: null,
                IsDeleted: false));

        public Task<ApiCredentialInfo> UpdateAsync(Guid tenantId, Guid credentialId, UpdateApiCredentialRequest request, CancellationToken ct = default)
            => GetAsync(tenantId, credentialId, ct);

        public Task<ApiCredentialInfo> UpdateRolesAsync(
            Guid tenantId,
            Guid credentialId,
            UpdateApiCredentialRolesRequest request,
            CancellationToken ct = default)
            => GetAsync(tenantId, credentialId, ct);

        public Task<ApiCredentialAccessContextDto> GetAccessAsync(Guid tenantId, Guid credentialId, string? requestedAgentKey = null, CancellationToken ct = default)
            => Task.FromResult(new ApiCredentialAccessContextDto(
                AccessSubjectTypes.ApiCredential,
                tenantId,
                credentialId,
                "Worker",
                "codex",
                "Codex",
                true,
                ["API"],
                [],
                [],
                ["work"],
                ["mcp"],
                ["codex"],
                new Dictionary<string, IReadOnlyList<ApiCredentialResourceAccessDto>>(),
                new ApiCredentialAccessCapabilitiesDto(true, true, true)));

        public Task<ApiCredentialAccessContextDto> UpdateAccessAsync(Guid tenantId, Guid credentialId, UpdateApiCredentialAccessRequest request, CancellationToken ct = default)
            => GetAccessAsync(tenantId, credentialId, null, ct);

        public Task<CreateApiCredentialResult> RotateAsync(Guid tenantId, Guid credentialId, CancellationToken ct = default)
            => Task.FromResult(new CreateApiCredentialResult
            {
                ApiKey = "ibk_rotated",
                Credential = GetAsync(tenantId, credentialId, ct).Result
            });

        public Task<ApiCredentialInfo> RevokeAsync(
            Guid tenantId,
            Guid credentialId,
            Guid? revokedByUserId,
            string? reason,
            CancellationToken ct = default)
            => GetAsync(tenantId, credentialId, ct);

        public Task<ApiCredentialInfo> ActivateAsync(Guid tenantId, Guid credentialId, CancellationToken ct = default)
            => GetAsync(tenantId, credentialId, ct);
    }

    private sealed class FakeApiCredentialAuthenticator : IApiCredentialAuthenticator
    {
        public Task<ApiCredentialAuthenticationResult> AuthenticateAsync(string apiKey, string? ipAddress = null, CancellationToken ct = default)
            => Task.FromResult(ApiCredentialAuthenticationResult.Fail("not_implemented"));
    }

    private sealed class FakeApiCredentialRoleCatalogProvider : IApiCredentialRoleCatalogProvider
    {
        public Task<IReadOnlyList<ApiCredentialRoleCatalogEntry>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ApiCredentialRoleCatalogEntry>>(
            [
                new("API", "API", "Base API credential role.", "base", true, false, true),
                new("tool:mcp", "MCP Tool Access", "Allows MCP tool access.", "mcp", true, false, true),
                new("api-scope:work", "Work", "Allows Work tools.", "module", true, false, true)
            ]);
    }

    private sealed class FakeApiCredentialScopeCatalogProvider : IApiCredentialScopeCatalogProvider
    {
        public Task<IReadOnlyList<ApiScopeCatalogItem>> GetScopesAsync(Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ApiScopeCatalogItem>>(
            [
                new("work", "Work API", "Allows Work API access.", "module", true, true, ModuleKey: "work"),
                new("mcp", "MCP Tools", "Allows MCP tool access.", "tool", true, false)
            ]);
    }

    private sealed class FakeApiCredentialAccessService : IApiCredentialAccessService
    {
        public Task<ApiCredentialAccessContextDto> BuildAccessContextAsync(ApiCredentialInfo credential, string? requestedAgentKey = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ApiCredentialContext?> GetCurrentApiCredentialAsync(ClaimsPrincipal principal, CancellationToken ct = default)
            => Task.FromResult<ApiCredentialContext?>(null);

        public Task<ApiCredentialAccessContextDto> GetCurrentAccessContextAsync(ClaimsPrincipal principal, string? requestedAgentKey = null, CancellationToken ct = default)
            => Task.FromResult(new ApiCredentialAccessContextDto(
                AccessSubjectTypes.ApiCredential,
                TenantId,
                Guid.NewGuid(),
                "Worker",
                "codex",
                "Codex",
                true,
                ["API"],
                [],
                [],
                ["work"],
                ["mcp"],
                ["codex"],
                new Dictionary<string, IReadOnlyList<ApiCredentialResourceAccessDto>>(),
                new ApiCredentialAccessCapabilitiesDto(true, true, true)));

        public Task<bool> HasApiScopeAsync(ClaimsPrincipal principal, string moduleKey, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> HasToolAccessAsync(ClaimsPrincipal principal, string toolKey, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> CanActAsAgentAsync(ClaimsPrincipal principal, string agentKey, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> CanCredentialActAsAgentAsync(Guid tenantId, Guid credentialId, string agentKey, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> HasResourceAccessAsync(ClaimsPrincipal principal, string resourceType, string resourceId, string minimumAccessLevel = AccessLevels.View, CancellationToken ct = default) => Task.FromResult(true);
        public Task RequireApiScopeAsync(ClaimsPrincipal principal, string moduleKey, CancellationToken ct = default) => Task.CompletedTask;
        public Task RequireToolAccessAsync(ClaimsPrincipal principal, string toolKey, CancellationToken ct = default) => Task.CompletedTask;
        public Task RequireAgentAccessAsync(ClaimsPrincipal principal, string agentKey, CancellationToken ct = default) => Task.CompletedTask;
        public Task RequireResourceAccessAsync(ClaimsPrincipal principal, string resourceType, string resourceId, string minimumAccessLevel = AccessLevels.View, CancellationToken ct = default) => Task.CompletedTask;
    }
}
