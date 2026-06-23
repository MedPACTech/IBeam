using IBeam.Licensing;
using IBeam.Licensing.Services;
using Microsoft.Extensions.Options;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace IBeam.Tests.Licensing;

[TestClass]
public sealed class LicensingServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("225925cc-995e-4584-a63b-4f2cb4f38f6f");

    [TestMethod]
    public async Task PlanCatalog_ReturnsConfiguredPlans()
    {
        var provider = CreatePlanCatalog();

        var plans = await provider.ListPlansAsync();

        Assert.HasCount(1, plans);
        Assert.AreEqual("hubbsly-work", plans[0].Key);
        CollectionAssert.Contains(plans[0].Entitlements.ToList(), "work:cards:create");
        Assert.AreEqual(2, plans[0].Limits["Seats"]);
    }

    [TestMethod]
    public async Task GrantLicense_MergesPlanEntitlementsLimitsAndMetadata()
    {
        var fixture = CreateFixture();

        var license = await fixture.Licenses.GrantLicenseAsync(
            TenantId,
            new GrantTenantLicenseRequest
            {
                PlanKey = "hubbsly-work",
                Entitlements = ["mcp:tools"],
                Limits = new Dictionary<string, int> { ["McpCallsPerMonth"] = 1000 },
                Metadata = new Dictionary<string, string> { ["contractNumber"] = "C-100" }
            });

        Assert.AreEqual("hubbsly-work", license.PlanKey);
        Assert.AreEqual(2, license.SeatLimit);
        CollectionAssert.Contains(license.Entitlements.ToList(), "work:cards:create");
        CollectionAssert.Contains(license.Entitlements.ToList(), "mcp:tools");
        Assert.AreEqual(1000, license.Limits["McpCallsPerMonth"]);
        Assert.AreEqual("C-100", license.Metadata["contractNumber"]);
    }

    [TestMethod]
    public async Task AssignSeatAsync_EnforcesSeatLimit()
    {
        var fixture = CreateFixture();
        var license = await fixture.Licenses.GrantLicenseAsync(
            TenantId,
            new GrantTenantLicenseRequest { PlanKey = "hubbsly-work" });

        await fixture.Assignments.AssignSeatAsync(
            TenantId,
            license.LicenseId,
            new AssignLicenseSeatRequest { Subject = new LicenseSubject(LicenseSubjectTypes.User, "user-1") });

        await fixture.Assignments.AssignSeatAsync(
            TenantId,
            license.LicenseId,
            new AssignLicenseSeatRequest { Subject = new LicenseSubject(LicenseSubjectTypes.User, "user-2") });

        await Assert.ThrowsExactlyAsync<LicensingException>(() =>
            fixture.Assignments.AssignSeatAsync(
                TenantId,
                license.LicenseId,
                new AssignLicenseSeatRequest { Subject = new LicenseSubject(LicenseSubjectTypes.User, "user-3") }));
    }

    [TestMethod]
    public async Task AuthorizeAsync_AllowsAssignedSubjectWithEntitlement()
    {
        var fixture = CreateFixture();
        var license = await fixture.Licenses.GrantLicenseAsync(
            TenantId,
            new GrantTenantLicenseRequest { PlanKey = "hubbsly-work" });

        var subject = new LicenseSubject(LicenseSubjectTypes.Agent, "codex");
        await fixture.Assignments.AssignSeatAsync(
            TenantId,
            license.LicenseId,
            new AssignLicenseSeatRequest { Subject = subject });

        var result = await fixture.Authorizer.AuthorizeAsync(TenantId, subject, "work:cards:create");

        Assert.IsTrue(result.Allowed);
        Assert.AreEqual(license.LicenseId, result.LicenseId);
    }

    [TestMethod]
    public async Task AuthorizeAsync_DeniesMissingEntitlement()
    {
        var fixture = CreateFixture();
        var license = await fixture.Licenses.GrantLicenseAsync(
            TenantId,
            new GrantTenantLicenseRequest { PlanKey = "hubbsly-work" });

        var subject = new LicenseSubject(LicenseSubjectTypes.Agent, "codex");
        await fixture.Assignments.AssignSeatAsync(
            TenantId,
            license.LicenseId,
            new AssignLicenseSeatRequest { Subject = subject });

        var result = await fixture.Authorizer.AuthorizeAsync(TenantId, subject, "money:close:update");

        Assert.IsFalse(result.Allowed);
    }

    [TestMethod]
    public async Task AuthorizeAsync_DeniesUnassignedSubjectWhenLicenseHasSeatLimit()
    {
        var fixture = CreateFixture();
        await fixture.Licenses.GrantLicenseAsync(
            TenantId,
            new GrantTenantLicenseRequest { PlanKey = "hubbsly-work" });

        var result = await fixture.Authorizer.AuthorizeAsync(
            TenantId,
            new LicenseSubject(LicenseSubjectTypes.Agent, "codex"),
            "work:cards:create");

        Assert.IsFalse(result.Allowed);
    }

    [TestMethod]
    public async Task AuthorizeAsync_DeniesExpiredLicense()
    {
        var fixture = CreateFixture();
        var subject = new LicenseSubject(LicenseSubjectTypes.User, "user-1");
        await fixture.Licenses.GrantLicenseAsync(
            TenantId,
            new GrantTenantLicenseRequest
            {
                PlanKey = "expired-work",
                Entitlements = ["work:cards:create"],
                StartsUtc = DateTimeOffset.UtcNow.AddDays(-10),
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(-1)
            });

        var result = await fixture.Authorizer.AuthorizeAsync(TenantId, subject, "work:cards:create");

        Assert.IsFalse(result.Allowed);
    }

    [TestMethod]
    public async Task RevokeLicenseAsync_RemovesAuthorization()
    {
        var fixture = CreateFixture();
        var license = await fixture.Licenses.GrantLicenseAsync(
            TenantId,
            new GrantTenantLicenseRequest { PlanKey = "hubbsly-work" });

        var subject = new LicenseSubject(LicenseSubjectTypes.User, "user-1");
        await fixture.Assignments.AssignSeatAsync(
            TenantId,
            license.LicenseId,
            new AssignLicenseSeatRequest { Subject = subject });

        await fixture.Licenses.RevokeLicenseAsync(TenantId, license.LicenseId, "cancelled");

        var result = await fixture.Authorizer.AuthorizeAsync(TenantId, subject, "work:cards:create");

        Assert.IsFalse(result.Allowed);
    }

    [TestMethod]
    public async Task RequireEntitlementAsync_ThrowsWhenDenied()
    {
        var fixture = CreateFixture();

        await Assert.ThrowsExactlyAsync<LicensingException>(() =>
            fixture.Authorizer.RequireEntitlementAsync(
                TenantId,
                new LicenseSubject(LicenseSubjectTypes.User, "user-1"),
                "work:cards:create"));
    }

    private static Fixture CreateFixture()
    {
        var store = new InMemoryLicensingStore();
        var catalog = CreatePlanCatalog();
        var licenses = new TenantLicenseService(store, catalog);
        var assignments = new LicenseSeatAssignmentService(store);
        var authorizer = new LicenseAuthorizer(store);

        return new Fixture(licenses, assignments, authorizer);
    }

    private static ConfigurationLicensePlanCatalogProvider CreatePlanCatalog()
        => new(Options.Create(new LicensingOptions
        {
            Plans =
            [
                new LicensePlanOptions
                {
                    Key = "hubbsly-work",
                    DisplayName = "Hubbsly Work",
                    Entitlements = ["feature:work", "work:cards:create", "work:cards:update"],
                    Limits = new Dictionary<string, int> { ["Seats"] = 2 },
                    Metadata = new Dictionary<string, string> { ["product"] = "hubbsly" }
                }
            ]
        }));

    private sealed record Fixture(
        TenantLicenseService Licenses,
        LicenseSeatAssignmentService Assignments,
        LicenseAuthorizer Authorizer);
}
