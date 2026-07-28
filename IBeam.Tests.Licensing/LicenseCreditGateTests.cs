using IBeam.Credits;
using IBeam.Credits.Services;
using IBeam.Licensing;
using IBeam.Licensing.Credits;
using IBeam.Licensing.Services;

namespace IBeam.Tests.Licensing;

[TestClass]
public sealed class LicenseCreditGateTests
{
    private static readonly Guid TenantId = Guid.Parse("d35c9f0a-c513-4a03-92d6-f0c176102129");
    private static readonly LicenseSubject Subject = new(LicenseSubjectTypes.User, "user-1");

    [TestMethod]
    public async Task ExecuteAsync_AllowsLicensedOperationAndSettlesMeasuredCredits()
    {
        var fixture = await CreateFixtureAsync(100);
        await GrantLicenseAsync(fixture, ["ai:chat"]);

        var result = await fixture.Gate.ExecuteAsync(
            CreateRequest(fixture, estimatedCredits: 20, maxCredits: 50),
            _ => Task.FromResult(new CreditMeasuredOperationResult<string>("completed", 30)));

        Assert.IsTrue(result.Allowed);
        Assert.IsTrue(result.OperationExecuted);
        Assert.AreEqual("completed", result.Value);
        Assert.AreEqual(30, result.CreditSettlement?.SettledAmount);
        Assert.AreEqual(70, result.CreditSettlement?.Balance?.Available);
    }

    [TestMethod]
    public async Task CheckAsync_ReturnsLicenseDenialWhenEntitlementIsMissing()
    {
        var fixture = await CreateFixtureAsync(100);
        await GrantLicenseAsync(fixture, ["notes:use"]);

        var result = await fixture.Gate.CheckAsync(CreateRequest(fixture));

        Assert.IsFalse(result.Allowed);
        Assert.AreEqual(LicenseCreditGateDenialScopes.License, result.DenialScope);
        Assert.AreEqual(LicenseGateDenialCodes.MissingEntitlement, result.DenialCode);
        Assert.IsNull(result.Credit);
    }

    [TestMethod]
    public async Task CheckAsync_ReturnsLicenseDenialWhenSeatIsMissing()
    {
        var fixture = await CreateFixtureAsync(100);
        await GrantLicenseAsync(fixture, ["ai:chat"], seatLimit: 1);

        var result = await fixture.Gate.CheckAsync(CreateRequest(fixture));

        Assert.IsFalse(result.Allowed);
        Assert.AreEqual(LicenseCreditGateDenialScopes.License, result.DenialScope);
        Assert.AreEqual(LicenseGateDenialCodes.MissingSeat, result.DenialCode);
    }

    [TestMethod]
    public async Task CheckAsync_ReturnsCreditDenialWhenReservationCannotBeCreated()
    {
        var fixture = await CreateFixtureAsync(10);
        await GrantLicenseAsync(fixture, ["ai:chat"]);

        var result = await fixture.Gate.CheckAsync(CreateRequest(fixture, estimatedCredits: 5, maxCredits: 20));

        Assert.IsFalse(result.Allowed);
        Assert.AreEqual(LicenseCreditGateDenialScopes.Credit, result.DenialScope);
        Assert.AreEqual(CreditPolicyDenialReasons.InsufficientCredits, result.DenialCode);
        Assert.IsTrue(result.License.Allowed);
    }

    [TestMethod]
    public async Task ExecuteAsync_ReleasesReservationWhenWrappedOperationFails()
    {
        var fixture = await CreateFixtureAsync(100);
        await GrantLicenseAsync(fixture, ["ai:chat"]);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => fixture.Gate.ExecuteAsync<string>(
                CreateRequest(fixture, estimatedCredits: 20, maxCredits: 50),
                _ => throw new InvalidOperationException("provider failed")));

        var reservations = await fixture.CreditStore.ListReservationsAsync(TenantId, fixture.CreditAccountId, "ai-chat");
        Assert.HasCount(1, reservations);
        Assert.AreEqual(CreditReservationStatuses.Released, reservations[0].Status);
        Assert.AreEqual("operation-failed", reservations[0].Metadata["releaseReason"]);
    }

    private static LicenseCreditGateRequest CreateRequest(Fixture fixture, decimal estimatedCredits = 10, decimal maxCredits = 25)
        => new()
        {
            TenantId = TenantId,
            Subject = Subject,
            Entitlement = "ai:chat",
            OperationName = "ai.chat.complete",
            CreditAccountId = fixture.CreditAccountId,
            CreditBucketKey = "ai-chat",
            EstimatedCredits = estimatedCredits,
            MaxCredits = maxCredits,
            CreditPolicyMode = CreditPolicyModes.StrictPrepaid,
            Metadata = new Dictionary<string, string> { ["idempotencyKey"] = Guid.NewGuid().ToString("N") }
        };

    private static async Task<TenantLicenseInfo> GrantLicenseAsync(Fixture fixture, string[] entitlements, int? seatLimit = null)
        => await fixture.Licenses.GrantLicenseAsync(
            TenantId,
            new GrantTenantLicenseRequest
            {
                PlanKey = "test",
                Entitlements = [.. entitlements],
                SeatLimit = seatLimit
            });

    private static async Task<Fixture> CreateFixtureAsync(decimal credits)
    {
        var licensingStore = new InMemoryLicensingStore();
        var licenses = new TenantLicenseService(licensingStore, new EmptyPlanCatalogProvider());
        var licenseGate = new LicenseGate(licensingStore);

        var creditStore = new InMemoryCreditStore();
        var reservations = new CreditReservationService(creditStore);
        var creditPolicy = new CreditPolicyService(reservations, reservations, creditStore);
        var gate = new LicenseCreditGate(licenseGate, creditPolicy, reservations);
        var creditAccountId = Guid.NewGuid();
        if (credits > 0)
        {
            var grant = CreditGrantInfo.Create(TenantId, creditAccountId, "ai-chat", credits, startsUtc: DateTimeOffset.UtcNow.AddMinutes(-1));
            await creditStore.AppendLedgerEntryAsync(grant.ToLedgerEntry());
        }

        return new Fixture(licenses, creditStore, gate, creditAccountId);
    }

    private sealed record Fixture(
        TenantLicenseService Licenses,
        InMemoryCreditStore CreditStore,
        LicenseCreditGate Gate,
        Guid CreditAccountId);

    private sealed class EmptyPlanCatalogProvider : ILicensePlanCatalogProvider
    {
        public Task<IReadOnlyList<LicensePlanInfo>> ListPlansAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LicensePlanInfo>>([]);

        public Task<LicensePlanInfo?> GetPlanAsync(string planKey, CancellationToken ct = default)
            => Task.FromResult<LicensePlanInfo?>(null);
    }
}
