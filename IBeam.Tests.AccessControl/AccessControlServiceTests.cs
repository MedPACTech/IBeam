using IBeam.AccessControl;
using IBeam.AccessControl.Services;
using Microsoft.Extensions.Options;
using System.Security.Claims;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace IBeam.Tests.AccessControl;

[TestClass]
public sealed class AccessControlServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("225925cc-995e-4584-a63b-4f2cb4f38f6f");

    [TestMethod]
    public async Task AuthorizeAsync_AllowsAssignedProject()
    {
        var fixture = CreateFixture();
        var subject = new AccessSubject(AccessSubjectTypes.User, "user-1");

        await fixture.Service.GrantAccessAsync(
            TenantId,
            new GrantResourceAccessRequest
            {
                ResourceType = "project",
                ResourceId = "project-1",
                Subject = subject,
                AccessLevel = ResourceAccessLevels.View
            });

        var result = await fixture.Authorizer.AuthorizeAsync(
            TenantId,
            "project",
            "project-1",
            subject,
            ResourceAccessLevels.View);

        Assert.IsTrue(result.Allowed);
    }

    [TestMethod]
    public async Task AuthorizeAsync_DeniesUnassignedProject()
    {
        var fixture = CreateFixture();
        var subject = new AccessSubject(AccessSubjectTypes.User, "user-4");

        await fixture.Service.GrantAccessAsync(
            TenantId,
            new GrantResourceAccessRequest
            {
                ResourceType = "project",
                ResourceId = "project-2",
                Subject = subject,
                AccessLevel = ResourceAccessLevels.View
            });

        var result = await fixture.Authorizer.AuthorizeAsync(
            TenantId,
            "project",
            "project-1",
            subject,
            ResourceAccessLevels.View);

        Assert.IsFalse(result.Allowed);
    }

    [TestMethod]
    public async Task AuthorizeAsync_AllowsHigherRankedAccessLevel()
    {
        var fixture = CreateFixture();
        var subject = new AccessSubject(AccessSubjectTypes.User, "user-1");

        await fixture.Service.GrantAccessAsync(
            TenantId,
            new GrantResourceAccessRequest
            {
                ResourceType = "project",
                ResourceId = "project-1",
                Subject = subject,
                AccessLevel = ResourceAccessLevels.Edit
            });

        var result = await fixture.Authorizer.AuthorizeAsync(
            TenantId,
            "project",
            "project-1",
            subject,
            ResourceAccessLevels.View);

        Assert.IsTrue(result.Allowed);
    }

    [TestMethod]
    public async Task AuthorizeAsync_DeniesLowerRankedAccessLevel()
    {
        var fixture = CreateFixture();
        var subject = new AccessSubject(AccessSubjectTypes.User, "user-1");

        await fixture.Service.GrantAccessAsync(
            TenantId,
            new GrantResourceAccessRequest
            {
                ResourceType = "project",
                ResourceId = "project-1",
                Subject = subject,
                AccessLevel = ResourceAccessLevels.View
            });

        var result = await fixture.Authorizer.AuthorizeAsync(
            TenantId,
            "project",
            "project-1",
            subject,
            ResourceAccessLevels.Edit);

        Assert.IsFalse(result.Allowed);
    }

    [TestMethod]
    public async Task AuthorizeAsync_AllowsWildcardResourceGrant()
    {
        var fixture = CreateFixture();
        var subject = new AccessSubject(AccessSubjectTypes.User, "project-admin");

        await fixture.Service.GrantAccessAsync(
            TenantId,
            new GrantResourceAccessRequest
            {
                ResourceType = "project",
                ResourceId = "*",
                Subject = subject,
                AccessLevel = ResourceAccessLevels.Admin
            });

        var result = await fixture.Authorizer.AuthorizeAsync(
            TenantId,
            "project",
            "new-project",
            subject,
            ResourceAccessLevels.Edit);

        Assert.IsTrue(result.Allowed);
    }

    [TestMethod]
    public async Task AuthorizeAsync_AllowsProjectAccessFromProductGrant()
    {
        var hierarchy = new TestResourceAccessHierarchyResolver();
        hierarchy.AddAncestor("project", "project-1", "product", "product-a");
        var fixture = CreateFixture(hierarchy);
        var subject = new AccessSubject(AccessSubjectTypes.User, "user-1");

        await fixture.Service.GrantAccessAsync(
            TenantId,
            new GrantResourceAccessRequest
            {
                ResourceType = "product",
                ResourceId = "product-a",
                Subject = subject,
                AccessLevel = ResourceAccessLevels.View
            });

        var result = await fixture.Authorizer.AuthorizeAsync(
            TenantId,
            "project",
            "project-1",
            subject,
            ResourceAccessLevels.View);

        Assert.IsTrue(result.Allowed);
    }

    [TestMethod]
    public async Task AuthorizeAsync_DoesNotAllowSiblingProjectFromProjectGrant()
    {
        var hierarchy = new TestResourceAccessHierarchyResolver();
        hierarchy.AddAncestor("project", "project-1", "product", "product-a");
        hierarchy.AddAncestor("project", "project-2", "product", "product-a");
        var fixture = CreateFixture(hierarchy);
        var subject = new AccessSubject(AccessSubjectTypes.User, "user-1");

        await fixture.Service.GrantAccessAsync(
            TenantId,
            new GrantResourceAccessRequest
            {
                ResourceType = "project",
                ResourceId = "project-1",
                Subject = subject,
                AccessLevel = ResourceAccessLevels.View
            });

        var result = await fixture.Authorizer.AuthorizeAsync(
            TenantId,
            "project",
            "project-2",
            subject,
            ResourceAccessLevels.View);

        Assert.IsFalse(result.Allowed);
    }

    [TestMethod]
    public async Task AuthorizeAsync_DoesNotTreatProjectGrantAsProductGrant()
    {
        var hierarchy = new TestResourceAccessHierarchyResolver();
        hierarchy.AddAncestor("project", "project-1", "product", "product-a");
        var fixture = CreateFixture(hierarchy);
        var subject = new AccessSubject(AccessSubjectTypes.User, "user-1");

        await fixture.Service.GrantAccessAsync(
            TenantId,
            new GrantResourceAccessRequest
            {
                ResourceType = "project",
                ResourceId = "project-1",
                Subject = subject,
                AccessLevel = ResourceAccessLevels.View
            });

        var result = await fixture.Authorizer.AuthorizeAsync(
            TenantId,
            "product",
            "product-a",
            subject,
            ResourceAccessLevels.View);

        Assert.IsFalse(result.Allowed);
    }

    [TestMethod]
    public async Task RevokeGrantAsync_RemovesAuthorization()
    {
        var fixture = CreateFixture();
        var subject = new AccessSubject(AccessSubjectTypes.User, "user-1");
        var grant = await fixture.Service.GrantAccessAsync(
            TenantId,
            new GrantResourceAccessRequest
            {
                ResourceType = "project",
                ResourceId = "project-1",
                Subject = subject,
                AccessLevel = ResourceAccessLevels.View
            });

        await fixture.Service.RevokeGrantAsync(TenantId, grant.GrantId);

        var result = await fixture.Authorizer.AuthorizeAsync(
            TenantId,
            "project",
            "project-1",
            subject,
            ResourceAccessLevels.View);

        Assert.IsFalse(result.Allowed);
    }

    [TestMethod]
    public async Task AuthorizeAsync_DeniesExpiredGrant()
    {
        var fixture = CreateFixture();
        var subject = new AccessSubject(AccessSubjectTypes.User, "user-1");

        await fixture.Service.GrantAccessAsync(
            TenantId,
            new GrantResourceAccessRequest
            {
                ResourceType = "project",
                ResourceId = "project-1",
                Subject = subject,
                AccessLevel = ResourceAccessLevels.View,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
            });

        var result = await fixture.Authorizer.AuthorizeAsync(
            TenantId,
            "project",
            "project-1",
            subject,
            ResourceAccessLevels.View);

        Assert.IsFalse(result.Allowed);
    }

    [TestMethod]
    public async Task ListGrantsAsync_FiltersBySubject()
    {
        var fixture = CreateFixture();
        var subject = new AccessSubject(AccessSubjectTypes.User, "user-4");

        await fixture.Service.GrantAccessAsync(
            TenantId,
            new GrantResourceAccessRequest
            {
                ResourceType = "project",
                ResourceId = "project-2",
                Subject = subject,
                AccessLevel = ResourceAccessLevels.View
            });

        await fixture.Service.GrantAccessAsync(
            TenantId,
            new GrantResourceAccessRequest
            {
                ResourceType = "project",
                ResourceId = "project-3",
                Subject = subject,
                AccessLevel = ResourceAccessLevels.Edit
            });

        var grants = await fixture.Service.ListGrantsAsync(TenantId, subject: subject);

        Assert.HasCount(2, grants);
    }

    [TestMethod]
    public async Task PermissionRoleAuthorizer_AllowsMappedRoleId()
    {
        var roleId = Guid.Parse("7f15d8b5-0797-42ce-83b4-019dd9854a7f");
        var store = new InMemoryPermissionRoleMapStore();
        await store.UpsertByPermissionNameAsync(
            TenantId,
            "users.manage",
            ["Administrator"],
            [roleId]);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("tid", TenantId.ToString("D")),
                new Claim("role", "Member"),
                new Claim("rid", roleId.ToString("D"))
            ],
            "test"));

        var authorizer = new PermissionRoleAuthorizer(store);
        var allowed = await authorizer.AuthorizeAsync(
            TenantId,
            principal,
            ["users.manage"],
            []);

        Assert.IsTrue(allowed);
    }

    [TestMethod]
    public async Task ServiceOperationAuthorizer_AllowsAndDeniesAccountingRole()
    {
        var store = new InMemoryServiceOperationPermissionStore();
        var service = new ServiceOperationPermissionService(store);
        var authorizer = CreateServiceOperationAuthorizer(store);
        var principal = PrincipalWithRoles("Accounting");

        await service.UpsertRuleAsync(TenantId, new UpsertServiceOperationPermissionRequest
        {
            Pattern = "pricing.*",
            Effect = ServiceOperationPermissionEffects.Allow,
            SubjectTypes = [AccessSubjectTypes.User],
            RoleNames = ["Accounting"]
        });

        await service.UpsertRuleAsync(TenantId, new UpsertServiceOperationPermissionRequest
        {
            Pattern = "sales.*",
            Effect = ServiceOperationPermissionEffects.Deny,
            SubjectTypes = [AccessSubjectTypes.User],
            RoleNames = ["Accounting"]
        });

        var pricing = await authorizer.AuthorizeAsync(new ServiceOperationAuthorizationRequest(
            TenantId,
            principal,
            "pricing.update"));

        var sales = await authorizer.AuthorizeAsync(new ServiceOperationAuthorizationRequest(
            TenantId,
            principal,
            "sales.delete"));

        Assert.IsTrue(pricing.Allowed);
        Assert.IsFalse(sales.Allowed);
    }

    [TestMethod]
    public async Task ServiceOperationAuthorizer_ExactDenyBeatsWildcardAllow()
    {
        var store = new InMemoryServiceOperationPermissionStore();
        var service = new ServiceOperationPermissionService(store);
        var authorizer = CreateServiceOperationAuthorizer(store);
        var principal = PrincipalWithRoles("Accounting");

        await service.UpsertRuleAsync(TenantId, new UpsertServiceOperationPermissionRequest
        {
            Pattern = "coupons.*",
            Effect = ServiceOperationPermissionEffects.Allow,
            RoleNames = ["Accounting"]
        });

        await service.UpsertRuleAsync(TenantId, new UpsertServiceOperationPermissionRequest
        {
            Pattern = "coupons.delete",
            Effect = ServiceOperationPermissionEffects.Deny,
            RoleNames = ["Accounting"]
        });

        var update = await authorizer.AuthorizeAsync(new ServiceOperationAuthorizationRequest(
            TenantId,
            principal,
            "coupons.update"));

        var delete = await authorizer.AuthorizeAsync(new ServiceOperationAuthorizationRequest(
            TenantId,
            principal,
            "coupons.delete"));

        Assert.IsTrue(update.Allowed);
        Assert.IsFalse(delete.Allowed);
        Assert.AreEqual("matched-deny-rule", delete.Reason);
    }

    [TestMethod]
    public async Task ServiceOperationAuthorizer_ConfigEmergencyDenyOverridesStoredAllow()
    {
        var store = new InMemoryServiceOperationPermissionStore();
        var service = new ServiceOperationPermissionService(store);
        await service.UpsertRuleAsync(TenantId, new UpsertServiceOperationPermissionRequest
        {
            Pattern = "referralcodes.delete",
            Effect = ServiceOperationPermissionEffects.Allow,
            RoleNames = ["Accounting"]
        });

        var options = new ServiceOperationAuthorizationOptions { Enabled = true };
        options.EmergencyOverrides.Add(new ServiceOperationPermissionRuleOptions
        {
            Pattern = "referralcodes.delete",
            Effect = ServiceOperationPermissionEffects.Deny,
            RoleNames = ["Accounting"]
        });

        var authorizer = CreateServiceOperationAuthorizer(store, options);
        var result = await authorizer.AuthorizeAsync(new ServiceOperationAuthorizationRequest(
            TenantId,
            PrincipalWithRoles("Accounting"),
            "referralcodes.delete"));

        Assert.IsFalse(result.Allowed);
        Assert.AreEqual(ServiceOperationPermissionSources.EmergencyConfiguration, result.MatchedRule?.Source);
    }

    [TestMethod]
    public async Task ServiceOperationAuthorizer_DoesNotTreatAgentAsUser()
    {
        var store = new InMemoryServiceOperationPermissionStore();
        var service = new ServiceOperationPermissionService(store);
        var authorizer = CreateServiceOperationAuthorizer(store);

        await service.UpsertRuleAsync(TenantId, new UpsertServiceOperationPermissionRequest
        {
            Pattern = "transactions.*",
            Effect = ServiceOperationPermissionEffects.Allow,
            SubjectTypes = [AccessSubjectTypes.User],
            RoleNames = ["Accounting"]
        });

        var user = await authorizer.AuthorizeAsync(new ServiceOperationAuthorizationRequest(
            TenantId,
            PrincipalWithRoles("Accounting"),
            "transactions.export"));

        var agent = await authorizer.AuthorizeAsync(new ServiceOperationAuthorizationRequest(
            TenantId,
            PrincipalWithRoles("Accounting", ("subject_type", AccessSubjectTypes.Agent)),
            "transactions.export"));

        Assert.IsTrue(user.Allowed);
        Assert.IsFalse(agent.Allowed);
    }

    private static Fixture CreateFixture(IResourceAccessHierarchyResolver? hierarchy = null)
    {
        var store = new InMemoryResourceAccessStore();
        var options = Options.Create(new AccessControlOptions());
        var service = new ResourceAccessService(store);
        var authorizer = new ResourceAccessAuthorizer(
            store,
            hierarchy ?? new NoOpResourceAccessHierarchyResolver(),
            options);

        return new Fixture(service, authorizer);
    }

    private static ServiceOperationAuthorizer CreateServiceOperationAuthorizer(
        IServiceOperationPermissionStore store,
        ServiceOperationAuthorizationOptions? options = null)
    {
        options ??= new ServiceOperationAuthorizationOptions { Enabled = true };
        options.Validate();
        return new ServiceOperationAuthorizer(
            [
                new ConfigServiceOperationPermissionRuleProvider(OptionsMonitor(options)),
                new StoreServiceOperationPermissionRuleProvider(store)
            ],
            OptionsMonitor(options));
    }

    private static IOptionsMonitor<T> OptionsMonitor<T>(T options)
        where T : class
        => new TestOptionsMonitor<T>(options);

    private static ClaimsPrincipal PrincipalWithRoles(
        string roleName,
        params (string Type, string Value)[] extraClaims)
    {
        var claims = new List<Claim>
        {
            new("tid", TenantId.ToString("D")),
            new("role", roleName)
        };

        claims.AddRange(extraClaims.Select(x => new Claim(x.Type, x.Value)));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private sealed record Fixture(
        ResourceAccessService Service,
        ResourceAccessAuthorizer Authorizer);

    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public TestOptionsMonitor(T currentValue)
        {
            CurrentValue = currentValue;
        }

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class TestResourceAccessHierarchyResolver : IResourceAccessHierarchyResolver
    {
        private readonly Dictionary<(string ResourceType, string ResourceId), List<ResourceAccessResource>> _ancestors =
            new();

        public void AddAncestor(string resourceType, string resourceId, string ancestorType, string ancestorId)
        {
            var key = (resourceType, resourceId);
            if (!_ancestors.TryGetValue(key, out var list))
            {
                list = [];
                _ancestors[key] = list;
            }

            list.Add(new ResourceAccessResource(ancestorType, ancestorId));
        }

        public Task<IReadOnlyList<ResourceAccessResource>> ListAncestorsAsync(
            Guid tenantId,
            string resourceType,
            string resourceId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResourceAccessResource>>(
                _ancestors.TryGetValue((resourceType, resourceId), out var ancestors)
                    ? ancestors
                    : []);
    }
}
