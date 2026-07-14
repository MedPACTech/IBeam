using System.Security.Claims;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Authorization;
using Microsoft.Extensions.Options;
using Moq;

namespace IBeam.Tests.Identity.Services;

[TestClass]
public sealed class IBeamAccessControlServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("2e658624-65ca-4492-9c8a-e0536e5bcf3c");
    private static readonly Guid RoleId = Guid.Parse("8796f728-ee09-4c83-9c2f-086e03ff5624");
    private static readonly string UserId = Guid.Parse("be0b8ac1-bd87-4a70-96a2-f0cd8950d2e3").ToString("D");

    [TestMethod]
    public async Task HasModuleAccessAsync_AllowsExplicitSubjectGrant()
    {
        var grant = new AccessGrant(
            Guid.NewGuid(),
            TenantId,
            AccessSubjectTypes.User,
            UserId,
            AccessResourceTypes.Module,
            "work",
            AccessLevels.View,
            true,
            DateTimeOffset.UtcNow);

        var sut = CreateSut(grants: [grant]);
        var principal = BuildPrincipal(new Claim("role", "Application"));

        var allowed = await sut.HasModuleAccessAsync(principal, "work");

        Assert.IsTrue(allowed);
    }

    [TestMethod]
    public async Task GetCurrentAccessContextAsync_MergesRolePermissionsAndGrants()
    {
        var grant = new AccessGrant(
            Guid.NewGuid(),
            TenantId,
            AccessSubjectTypes.User,
            UserId,
            "project",
            "project-1",
            AccessLevels.Edit,
            true,
            DateTimeOffset.UtcNow);

        var permissionResolver = CreatePermissionResolver(new PermissionGrantSet(["Application"], []));
        var catalog = new FakePermissionCatalogProvider(
        [
            new ExposedPermission(
                "work.view",
                null,
                "configuration:catalog",
                "Work",
                ModuleKey: "work")
        ]);

        var sut = CreateSut(grants: [grant], catalog: catalog, permissionResolver: permissionResolver.Object);
        var principal = BuildPrincipal(
            new Claim("role", "Application"),
            new Claim("rid", RoleId.ToString("D")));

        var context = await sut.GetCurrentAccessContextAsync(principal);

        Assert.IsTrue(context.Permissions.Contains("work.view"));
        Assert.IsTrue(context.Roles.Contains("Application"));
        Assert.IsTrue(context.RoleIds.Contains(RoleId));
        Assert.IsTrue(context.Resources["project"].Any(x => x.ResourceId == "project-1" && x.AccessLevel == AccessLevels.Edit));
    }

    [TestMethod]
    public async Task OwnerRole_GetsConfiguredModulesAtManageAccess()
    {
        var sut = CreateSut();
        var principal = BuildPrincipal(new Claim("role", "Owner"));

        var context = await sut.GetCurrentAccessContextAsync(principal);

        Assert.IsTrue(context.Modules.Any(x => x.Module == "work" && x.AccessLevel == AccessLevels.Manage));
        Assert.IsTrue(context.Capabilities.CanAssignOwner);
    }

    [TestMethod]
    public async Task GetAccessCatalogAsync_MergesDefaultsProvidersAndTenantOverrides()
    {
        var overrideItem = new AccessCatalogOverride(
            Guid.NewGuid(),
            TenantId,
            "project:platform",
            "Platform",
            null,
            AccessCatalogCategories.Resource,
            true,
            true,
            true,
            [AccessSubjectTypes.User, AccessSubjectTypes.ApiCredential],
            "project",
            "platform",
            "product",
            "hubbsly",
            [AccessLevels.View, AccessLevels.Edit],
            null,
            null,
            null,
            false,
            null,
            DateTimeOffset.UtcNow);

        var sut = CreateSut(
            catalogOverrides: [overrideItem],
            catalogItemProviders:
            [
                new FakeAccessCatalogItemProvider(
                [
                    new AccessCatalogItem(
                        "atlas",
                        "Atlas",
                        null,
                        AccessCatalogCategories.Agent,
                        AccessCatalogSources.HostProvider,
                        true,
                        false,
                        true,
                        [AccessSubjectTypes.ApiCredential])
                ])
            ]);

        var catalog = await sut.GetAccessCatalogAsync(TenantId);

        Assert.IsTrue(catalog.Roles.Any(x => x.Key == "Owner" && x.Source == AccessCatalogSources.IBeamDefault));
        Assert.IsTrue(catalog.Modules.Any(x => x.Key == "work" && x.Source == AccessCatalogSources.HostConfig));
        Assert.IsTrue(catalog.ApiScopes.Any(x => x.Key == "work"));
        Assert.IsTrue(catalog.Operations.Any(x => x.Key == "projects.delete" && x.IsDangerous));
        Assert.IsTrue(catalog.Agents.Any(x => x.Key == "atlas"));
        Assert.IsTrue(catalog.Resources.Any(x =>
            x.Key == "project:platform" &&
            x.Source == AccessCatalogSources.TenantDb &&
            x.ParentResourceType == "product"));
    }

    private static IBeamAccessControlService CreateSut(
        IReadOnlyList<AccessGrant>? grants = null,
        IReadOnlyList<AccessCatalogOverride>? catalogOverrides = null,
        IPermissionCatalogProvider? catalog = null,
        IPermissionGrantResolver? permissionResolver = null,
        IReadOnlyList<IIBeamAccessCatalogItemProvider>? catalogItemProviders = null)
    {
        var options = new IBeamAccessControlOptions
        {
            Modules =
            [
                new AccessModuleDefinition(
                    "work",
                    "Work",
                    SupportedAccessLevels: [AccessLevels.View, AccessLevels.Edit, AccessLevels.Manage],
                    ImpliedByPermissionNames: ["work.view"])
            ]
        };
        options.Resources.Add<TestProject>("project", "projects", label: "Project", module: "products");

        return new IBeamAccessControlService(
            new FakeAccessGrantStore(grants ?? Array.Empty<AccessGrant>()),
            new FakeAccessCatalogOverrideStore(catalogOverrides ?? Array.Empty<AccessCatalogOverride>()),
            catalog ?? new FakePermissionCatalogProvider(Array.Empty<ExposedPermission>()),
            new OperationCatalogProvider(new StaticOptionsMonitor<IBeamAccessControlOptions>(options)),
            new FakeApiCredentialScopeCatalogProvider(),
            permissionResolver ?? CreatePermissionResolver(PermissionGrantSet.Empty).Object,
            new StaticOptionsMonitor<IBeamAccessControlOptions>(options),
            Array.Empty<IIBeamAccessCatalogProvider>(),
            catalogItemProviders ?? Array.Empty<IIBeamAccessCatalogItemProvider>(),
            Array.Empty<IAgentCatalogProvider>(),
            Array.Empty<IIBeamAccessRuleProvider>());
    }

    private static Mock<IPermissionGrantResolver> CreatePermissionResolver(PermissionGrantSet grants)
    {
        var resolver = new Mock<IPermissionGrantResolver>(MockBehavior.Strict);
        resolver.Setup(x => x.ResolveAsync(
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(grants);
        return resolver;
    }

    private static ClaimsPrincipal BuildPrincipal(params Claim[] extraClaims)
    {
        var claims = new List<Claim>
        {
            new("tid", TenantId.ToString("D")),
            new("sub", UserId)
        };
        claims.AddRange(extraClaims);
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "unit-test"));
    }

    private sealed class FakeAccessGrantStore : IIBeamAccessGrantStore
    {
        private readonly IReadOnlyList<AccessGrant> _grants;

        public FakeAccessGrantStore(IReadOnlyList<AccessGrant> grants)
        {
            _grants = grants;
        }

        public Task<IReadOnlyList<AccessGrant>> GetGrantsAsync(Guid tenantId, string? subjectType = null, string? subjectId = null, CancellationToken ct = default)
        {
            var grants = _grants
                .Where(x => x.TenantId == tenantId)
                .Where(x => string.IsNullOrWhiteSpace(subjectType) || string.Equals(x.SubjectType, subjectType, StringComparison.OrdinalIgnoreCase))
                .Where(x => string.IsNullOrWhiteSpace(subjectId) || string.Equals(x.SubjectId, subjectId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return Task.FromResult<IReadOnlyList<AccessGrant>>(grants);
        }

        public Task<AccessGrant?> GetGrantAsync(Guid tenantId, Guid grantId, CancellationToken ct = default)
            => Task.FromResult(_grants.FirstOrDefault(x => x.TenantId == tenantId && x.GrantId == grantId));

        public Task<AccessGrant> UpsertGrantAsync(Guid tenantId, Guid? grantId, string subjectType, string subjectId, string resourceType, string resourceId, string accessLevel, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task DeleteGrantAsync(Guid tenantId, Guid grantId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeAccessCatalogOverrideStore : IIBeamAccessCatalogOverrideStore
    {
        private readonly IReadOnlyList<AccessCatalogOverride> _overrides;

        public FakeAccessCatalogOverrideStore(IReadOnlyList<AccessCatalogOverride> overrides)
        {
            _overrides = overrides;
        }

        public Task<IReadOnlyList<AccessCatalogOverride>> GetOverridesAsync(Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AccessCatalogOverride>>(_overrides.Where(x => x.TenantId == tenantId).ToList());

        public Task<AccessCatalogOverride?> GetOverrideAsync(Guid tenantId, Guid catalogItemId, CancellationToken ct = default)
            => Task.FromResult(_overrides.FirstOrDefault(x => x.TenantId == tenantId && x.CatalogItemId == catalogItemId));

        public Task<AccessCatalogOverride> UpsertOverrideAsync(Guid tenantId, Guid? catalogItemId, UpsertAccessCatalogOverrideRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task DeleteOverrideAsync(Guid tenantId, Guid catalogItemId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeApiCredentialScopeCatalogProvider : IApiCredentialScopeCatalogProvider
    {
        public Task<IReadOnlyList<ApiScopeCatalogItem>> GetScopesAsync(Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ApiScopeCatalogItem>>(
            [
                new ApiScopeCatalogItem("work", "Work API", "Allows Work API calls.", "module", true, true, ModuleKey: "work"),
                new ApiScopeCatalogItem("mcp", "MCP", "Allows MCP tool calls.", "tool", true, false)
            ]);
    }

    private sealed class FakeAccessCatalogItemProvider : IIBeamAccessCatalogItemProvider
    {
        private readonly IReadOnlyList<AccessCatalogItem> _items;

        public FakeAccessCatalogItemProvider(IReadOnlyList<AccessCatalogItem> items)
        {
            _items = items;
        }

        public Task<IReadOnlyList<AccessCatalogItem>> GetCatalogItemsAsync(Guid tenantId, CancellationToken ct = default)
            => Task.FromResult(_items);
    }

    private sealed class FakePermissionCatalogProvider : IPermissionCatalogProvider
    {
        private readonly IReadOnlyList<ExposedPermission> _permissions;

        public FakePermissionCatalogProvider(IReadOnlyList<ExposedPermission> permissions)
        {
            _permissions = permissions;
        }

        public Task<IReadOnlyList<ExposedPermission>> GetExposedPermissionsAsync(CancellationToken ct = default)
            => Task.FromResult(_permissions);
    }

    private sealed class TestProject
    {
    }

    private sealed class OperationFixture
    {
        [IBeam.Identity.Authorization.IBeamOperation(
            "projects.delete",
            Label = "Delete Project",
            Module = "products",
            ResourceType = "project",
            RequiredAccessLevel = AccessLevels.Manage,
            Category = "projects",
            IsDangerous = true)]
        public Task DeleteProjectAsync(Guid projectId, CancellationToken ct = default) => Task.CompletedTask;

        [IBeam.Identity.Authorization.IBeamOperationTemplate(
            "{permissionPrefix}.delete",
            Operation = "delete",
            IsDangerous = true)]
        [IBeam.Identity.Authorization.IBeamResourceAccessTemplate("{resourceKey}", "id", AccessLevels.Manage)]
        public Task DeleteAsync<T>(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T value) => CurrentValue = value;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
