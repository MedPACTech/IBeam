using IBeam.AccessControl;
using IBeam.AccessControl.Services;
using IBeam.Identity.Interfaces;
using System.Text.Json;
using Microsoft.Extensions.Options;

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
    public async Task ResourceAccessClaimsEnricher_EmitsActiveUserGrants()
    {
        var fixture = CreateFixture();
        var userId = Guid.Parse("c52c07b9-f04d-5d49-ae72-2521a261f021");
        var subject = new AccessSubject(AccessSubjectTypes.User, userId.ToString("D"));

        await fixture.Service.GrantAccessAsync(
            TenantId,
            new GrantResourceAccessRequest
            {
                ResourceType = "product",
                ResourceId = "qurvia",
                Subject = subject,
                AccessLevel = ResourceAccessLevels.Manage
            });

        var revoked = await fixture.Service.GrantAccessAsync(
            TenantId,
            new GrantResourceAccessRequest
            {
                ResourceType = "project",
                ResourceId = "project-1",
                Subject = subject,
                AccessLevel = ResourceAccessLevels.Delete
            });

        await fixture.Service.RevokeGrantAsync(TenantId, revoked.GrantId);

        var enricher = new ResourceAccessClaimsEnricher(
            fixture.Service,
            Options.Create(new AccessControlOptions()));

        var claims = await enricher.EnrichAsync(
            new ClaimsEnrichmentContext(userId, TenantId, []));

        var claim = claims.Single(x => x.Type == ResourceAccessClaimTypes.ResourceAccess);
        using var document = JsonDocument.Parse(claim.Value);
        var grants = document.RootElement.GetProperty("grants");

        Assert.AreEqual("json", claim.ValueType);
        Assert.AreEqual(1, grants.GetArrayLength());
        Assert.AreEqual("product", grants[0].GetProperty("resourceType").GetString());
        Assert.AreEqual("qurvia", grants[0].GetProperty("resourceId").GetString());
        Assert.AreEqual(ResourceAccessLevels.Manage, grants[0].GetProperty("accessLevel").GetString());
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

    private sealed record Fixture(
        ResourceAccessService Service,
        ResourceAccessAuthorizer Authorizer);

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
