using IBeam.AccessControl;
using IBeam.AccessControl.Services;
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

    private static Fixture CreateFixture()
    {
        var store = new InMemoryResourceAccessStore();
        var options = Options.Create(new AccessControlOptions());
        var service = new ResourceAccessService(store);
        var authorizer = new ResourceAccessAuthorizer(store, options);

        return new Fixture(service, authorizer);
    }

    private sealed record Fixture(
        ResourceAccessService Service,
        ResourceAccessAuthorizer Authorizer);
}
