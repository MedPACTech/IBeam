using System.Text.Json;
using IBeam.Licensing;
using IBeam.Licensing.Services;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Licensing;
using IBeam.Management.Operations;
using IBeam.Services.Abstractions;
using Moq;

namespace IBeam.Tests.Management.Licensing;

[TestClass]
public sealed class SeatAdministrationTests
{
    [TestMethod]
    public async Task AssignUnassignUseExplicitUserAndAssignmentWithoutChangingLicenceContents()
    {
        var f = await Fixture.Create(); var before = (await f.Store.GetLicenseAsync(f.Context.TenantId, f.Context.ResourceId))!;
        var assigned = await f.Assign(f.User);
        Assert.AreEqual("assigned", assigned.Disposition); Assert.AreEqual("succeeded", assigned.Outcome); Assert.IsNotNull(assigned.AssignmentId);
        var read = await f.Target.ReadAsync(f.Context);
        Assert.AreEqual(1, read.AssignedCount); Assert.AreEqual(f.User, read.Assignments.Single().UserId);
        var current = f.Context with { OperationId = Guid.NewGuid(), ResourceVersion = read.Version };
        var unassigned = await f.Target.ChangeAsync(ManagementSeatOperations.Unassign, current, current.ResourceId, f.User, assigned.AssignmentId, read.Version);
        Assert.AreEqual("unassigned", unassigned.Disposition); Assert.HasCount(0, (await f.Target.ReadAsync(f.Context)).Assignments);
        var after = (await f.Store.GetLicenseAsync(f.Context.TenantId, f.Context.ResourceId))!;
        Assert.AreEqual(before.Status, after.Status); Assert.AreEqual(before.PlanKey, after.PlanKey); Assert.AreEqual(before.SeatLimit, after.SeatLimit);
        Assert.AreEqual(before.ExpiresUtc, after.ExpiresUtc); Assert.AreEqual(JsonSerializer.Serialize(before.Entitlements), JsonSerializer.Serialize(after.Entitlements));
        Assert.AreEqual(JsonSerializer.Serialize(before.Metadata), JsonSerializer.Serialize(after.Metadata));
    }

    [TestMethod]
    [DataRow("memberships-only")] [DataRow("missing-read")] [DataRow("machine")] [DataRow("revoked")]
    [DataRow("application")] [DataRow("realm")] [DataRow("tenant")] [DataRow("user")]
    public async Task LiveIndependentAuthorityPrecedesNativeAccess(string change)
    {
        var f = await Fixture.Create(); var context = f.Context;
        switch (change)
        {
            case "memberships-only": f.Access.Human = [ManagementCustomerIdentityOperations.MembershipsManage]; break;
            case "missing-read": f.Access.Human = [ManagementSeatOperations.Assign]; break;
            case "machine": f.Access.Machine = [ManagementSeatOperations.Read]; break;
            case "revoked": f.Access.Revoked = true; break;
            case "application": context = context with { ApplicationId = Guid.NewGuid() }; break;
            case "realm": context = context with { RealmId = Guid.NewGuid() }; break;
            case "tenant": context = context with { TenantId = Guid.NewGuid() }; break;
            case "user": context = context with { ActorUserId = Guid.NewGuid() }; break;
        }
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Target.ChangeAsync(ManagementSeatOperations.Assign, context,
            context.ResourceId, f.User, null, context.ResourceVersion!));
        Assert.AreEqual(0, f.Store.Commits);
    }

    [TestMethod]
    [DataRow("disabled-member")] [DataRow("foreign-member")] [DataRow("expired")]
    [DataRow("suspended")] [DataRow("revoked-license")] [DataRow("full")]
    public async Task NativeEligibilityAndCapacityRejectBeforeMutation(string change)
    {
        var f = await Fixture.Create(seats: change == "full" ? 0 : 1);
        if (change == "disabled-member") f.Subjects.Disabled.Add(f.User);
        if (change == "foreign-member") f.Subjects.Users.Remove(f.User);
        if (change == "expired") f.Clock.Now = f.Clock.Now.AddDays(5);
        if (change == "suspended") await f.Licenses.UpdateLicenseAsync(f.Context.TenantId, f.Context.ResourceId, new() { Status = "suspended" });
        if (change == "revoked-license") await f.Licenses.RevokeLicenseAsync(f.Context.TenantId, f.Context.ResourceId, "fixture");
        var snapshot = await f.Native.ReadManagementSeatsAsync(f.Context.TenantId, f.Context.ResourceId);
        var context = f.Context with { ResourceVersion = snapshot.Version };
        await Assert.ThrowsAsync<ManagementOperationConflictException>(() => f.Target.ChangeAsync(ManagementSeatOperations.Assign, context,
            context.ResourceId, f.User, null, context.ResourceVersion!));
        Assert.AreEqual(0, f.Store.Commits); Assert.HasCount(0, (await f.Target.ReadAsync(f.Context)).Assignments);
    }

    [TestMethod]
    public async Task SameIntentCannotConsumeAnotherSeatOrRevokeAnotherUsersAssignment()
    {
        var f = await Fixture.Create(seats: 3);
        var first = await f.Assign(f.User);
        Assert.AreEqual(first, await f.Assign(f.User));
        var read = await f.Target.ReadAsync(f.Context);
        var other = Guid.NewGuid(); f.Subjects.Users.Add(other);
        var changed = f.Context with { ResourceVersion = read.Version };
        await Assert.ThrowsAsync<ManagementOperationConflictException>(() => f.Target.ChangeAsync(ManagementSeatOperations.Assign, changed,
            changed.ResourceId, other, null, read.Version));
        var unassign = f.Context with { OperationId = Guid.NewGuid(), ResourceVersion = read.Version };
        await Assert.ThrowsAsync<ManagementOperationConflictException>(() => f.Target.ChangeAsync(ManagementSeatOperations.Unassign, unassign,
            unassign.ResourceId, other, first.AssignmentId, read.Version));
        Assert.HasCount(1, (await f.Target.ReadAsync(f.Context)).Assignments);
    }

    [TestMethod]
    public async Task LostReplyPollAndHistoricalReplayNeverRecreateRemovedSeat()
    {
        var f = await Fixture.Create(); f.Store.LoseReply = true;
        var unknown = await f.Assign(f.User);
        Assert.AreEqual("unknown", unknown.Outcome); Assert.IsNull(unknown.AssignmentId); Assert.IsNull(unknown.Version);
        var known = await f.Target.FindAsync(ManagementSeatOperations.Assign, f.Context);
        Assert.AreEqual("assigned", known.Disposition); Assert.AreEqual(1, f.Store.Commits);
        var revoke = f.Context with { OperationId = Guid.NewGuid(), ResourceVersion = known.Version };
        await f.Target.ChangeAsync(ManagementSeatOperations.Unassign, revoke, revoke.ResourceId, f.User, known.AssignmentId, known.Version!);
        var replay = await f.Assign(f.User);
        Assert.AreEqual(known, replay); Assert.HasCount(0, (await f.Target.ReadAsync(f.Context)).Assignments);
        Assert.DoesNotContain("PROVIDER-PRIVATE-CANARY", JsonSerializer.Serialize(new[] { unknown, known, replay }));
    }

    [TestMethod]
    public async Task SimultaneousAssignmentsFenceOneRemainingSeat()
    {
        var f = await Fixture.Create(); f.Store.Barrier = new(8);
        var users = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToArray(); foreach (var user in users) f.Subjects.Users.Add(user);
        var results = await Task.WhenAll(users.Select(async user =>
        {
            var context = f.Context with { OperationId = Guid.NewGuid() };
            try { await f.Target.ChangeAsync(ManagementSeatOperations.Assign, context, context.ResourceId, user, null, context.ResourceVersion!); return "assigned"; }
            catch (ManagementOperationPreconditionException) { return "stale"; }
        }));
        Assert.HasCount(1, results.Where(x => x == "assigned")); Assert.HasCount(7, results.Where(x => x == "stale"));
        Assert.HasCount(1, (await f.Target.ReadAsync(f.Context)).Assignments);
    }

    [TestMethod]
    public async Task DuplicateConcurrentIntentCreatesOneSeatAndOneStableReceipt()
    {
        var f = await Fixture.Create(); f.Store.Barrier = new(8);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => f.Assign(f.User)));
        Assert.HasCount(1, results.Select(x => x.AssignmentId).Distinct()); Assert.HasCount(1, results.Select(x => x.Version).Distinct());
        Assert.HasCount(1, (await f.Target.ReadAsync(f.Context)).Assignments);
    }

    [TestMethod]
    public async Task NativeLegacyWritersAndLicenceUpdatesParticipateInManagedRevision()
    {
        var f = await Fixture.Create(seats: 3); var old = (await f.Store.GetLicenseAsync(f.Context.TenantId, f.Context.ResourceId))!;
        var assigned = await f.Assign(f.User);
        await Assert.ThrowsAsync<LicenseStatusPreconditionException>(() => f.Store.UpsertLicenseAsync(old with { SeatLimit = 100 }));
        var rogue = new LicenseSeatAssignmentInfo(Guid.NewGuid(), old.TenantId, old.LicenseId, new("user", Guid.NewGuid().ToString("D")), f.Clock.Now, null, new Dictionary<string, string>());
        await Assert.ThrowsAsync<LicenseSeatPreconditionException>(() => f.Store.AddAssignmentAsync(rogue));
        await Assert.ThrowsAsync<LicenseSeatPreconditionException>(() => f.Store.DeleteAssignmentAsync(old.TenantId, old.LicenseId, assigned.AssignmentId!.Value));
        var legacy = new LicenseSeatAssignmentService(f.Store);
        var other = await legacy.AssignSeatAsync(old.TenantId, old.LicenseId, new() { Subject = new("api-credential", "PRIVATE-CREDENTIAL-SUBJECT", "PRIVATE-PROFILE") });
        var read = await f.Target.ReadAsync(f.Context);
        Assert.AreEqual(2, read.AssignedCount); Assert.IsNull(read.Assignments.Single(x => x.AssignmentId == other.AssignmentId).UserId);
        Assert.DoesNotContain("PRIVATE-", JsonSerializer.Serialize(read));
        await legacy.RevokeSeatAsync(old.TenantId, old.LicenseId, other.AssignmentId);
        Assert.AreEqual(1, (await f.Target.ReadAsync(f.Context)).AssignedCount);
        var stale = f.Context with { OperationId = Guid.NewGuid(), ResourceVersion = assigned.Version };
        await Assert.ThrowsAsync<ManagementOperationPreconditionException>(() => f.Target.ChangeAsync(ManagementSeatOperations.Unassign, stale, stale.ResourceId,
            f.User, assigned.AssignmentId, stale.ResourceVersion!));
    }

    [TestMethod]
    public async Task DisabledCustomerCanBeUnassignedUnderExplicitNativeCleanupPolicy()
    {
        var f = await Fixture.Create(); var assigned = await f.Assign(f.User); f.Subjects.Disabled.Add(f.User);
        var context = f.Context with { OperationId = Guid.NewGuid(), ResourceVersion = assigned.Version };
        await f.Target.ChangeAsync(ManagementSeatOperations.Unassign, context, context.ResourceId, f.User, assigned.AssignmentId, context.ResourceVersion!);
        Assert.HasCount(0, (await f.Target.ReadAsync(f.Context)).Assignments); Assert.Contains(f.User, f.Subjects.Disabled);
    }

    [TestMethod]
    public async Task AlreadyAssignedNewIntentIsNoopAndOldUnassignCannotDeleteNewAssignment()
    {
        var f = await Fixture.Create(); var first = await f.Assign(f.User);
        var sameUser = f.Context with { OperationId = Guid.NewGuid(), ResourceVersion = first.Version };
        var noop = await f.Target.ChangeAsync(ManagementSeatOperations.Assign, sameUser, sameUser.ResourceId, f.User, null, sameUser.ResourceVersion!);
        Assert.AreEqual("already-assigned", noop.Disposition); Assert.AreEqual(first.AssignmentId, noop.AssignmentId); Assert.AreEqual(first.Version, noop.Version);
        var revoke = f.Context with { OperationId = Guid.NewGuid(), ResourceVersion = first.Version };
        var removed = await f.Target.ChangeAsync(ManagementSeatOperations.Unassign, revoke, revoke.ResourceId, f.User, first.AssignmentId, first.Version!);
        var again = f.Context with { OperationId = Guid.NewGuid(), ResourceVersion = removed.Version };
        var fresh = await f.Target.ChangeAsync(ManagementSeatOperations.Assign, again, again.ResourceId, f.User, null, again.ResourceVersion!);
        Assert.AreNotEqual(first.AssignmentId, fresh.AssignmentId);
        await f.Target.ChangeAsync(ManagementSeatOperations.Unassign, revoke, revoke.ResourceId, f.User, first.AssignmentId, first.Version!);
        Assert.AreEqual(fresh.AssignmentId, (await f.Target.ReadAsync(f.Context)).Assignments.Single().AssignmentId);
    }

    [TestMethod]
    public async Task ProtectedLegacyUserWritesKeepNativeEligibilityAndDoNotCreateCustomerPrincipals()
    {
        var f = await Fixture.Create(seats: 3); await f.Assign(f.User);
        var other = Guid.NewGuid(); f.Subjects.Users.Add(other); f.Subjects.Disabled.Add(other);
        var request = new AssignLicenseSeatRequest { Subject = new("user", other.ToString("D"), "PRIVATE-PROFILE") };
        await Assert.ThrowsAsync<LicenseSeatConflictException>(() => f.Native.AssignSeatAsync(f.Context.TenantId, f.Context.ResourceId, request));
        await Assert.ThrowsAsync<LicenseSeatConflictException>(() => new LicenseSeatAssignmentService(f.Store).AssignSeatAsync(f.Context.TenantId, f.Context.ResourceId, request));
        f.Subjects.Disabled.Remove(other);
        var assigned = await f.Native.AssignSeatAsync(f.Context.TenantId, f.Context.ResourceId, request);
        f.Subjects.Disabled.Add(other);
        await f.Native.RevokeSeatAsync(f.Context.TenantId, f.Context.ResourceId, assigned.AssignmentId);
        Assert.AreEqual(1, (await f.Target.ReadAsync(f.Context)).AssignedCount); Assert.Contains(other, f.Subjects.Disabled);
    }

    [TestMethod]
    public async Task MaximumBoundedSnapshotFitsTypedPayloadLimitAndHidesNativeProfiles()
    {
        var f = await Fixture.Create(seats: 2000);
        for (var index = 0; index < 2000; index++) await f.Store.AddAssignmentAsync(new(Guid.NewGuid(), f.Context.TenantId, f.Context.ResourceId,
            new("user", Guid.NewGuid().ToString("D"), "PRIVATE-PROFILE-CANARY"), f.Clock.Now, null, new Dictionary<string, string> { ["provider"] = "PRIVATE-PROFILE-CANARY" }));
        var read = await f.Target.ReadAsync(f.Context);
        var codec = new ManagementOperationPayloadCodec(new(ManagementSeatServiceExtensions.SeatDescriptors));
        var bytes = codec.WriteResult(ManagementSeatOperations.Read, read);
        Assert.IsLessThanOrEqualTo(262144, bytes.Length); Assert.AreEqual(2000, read.AssignedCount);
        Assert.DoesNotContain("PRIVATE-PROFILE-CANARY", System.Text.Encoding.UTF8.GetString(bytes));
        var other = Guid.NewGuid(); f.Subjects.Users.Add(other);
        var full = f.Context with { OperationId = Guid.NewGuid(), ResourceVersion = read.Version };
        await Assert.ThrowsAsync<ManagementOperationConflictException>(() => f.Target.ChangeAsync(ManagementSeatOperations.Assign, full, full.ResourceId, other, null, full.ResourceVersion!));
        Assert.IsFalse(await f.Store.RequiresSeatProtectionAsync(full.TenantId, full.ResourceId));
    }

    internal sealed class Fixture
    {
        public LicenseStatusTests.FaultStore Store { get; } = new();
        public LicenseStatusTests.Clock Clock { get; } = new();
        public Subjects Subjects { get; } = new();
        public Access Access { get; } = new();
        public Guid User { get; } = Guid.NewGuid();
        public TenantLicenseService Licenses { get; private set; } = null!;
        public LicenseSeatAssignmentService Native { get; private set; } = null!;
        public ManagementSeatTargetService Target { get; private set; } = null!;
        public ManagementOperationContext Context { get; private set; } = null!;
        public static async Task<Fixture> Create(int seats = 1)
        {
            var f = new Fixture(); var catalog = new Mock<ILicensePlanCatalogProvider>();
            catalog.Setup(x => x.GetPlanAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((LicensePlanInfo?)null);
            f.Licenses = new(f.Store, catalog.Object);
            var tenant = Guid.NewGuid(); var license = await f.Licenses.GrantLicenseAsync(tenant, new() { PlanKey = "synthetic", SeatLimit = seats,
                StartsUtc = f.Clock.Now.AddDays(-1), ExpiresUtc = f.Clock.Now.AddHours(1), Entitlements = ["synthetic"], Metadata = new() { ["fixture"] = "safe" } });
            f.Subjects.Tenant = tenant; f.Subjects.Users.Add(f.User);
            var admin = new TenantLicenseSeatAdministrationService(f.Store, f.Subjects, new ServiceOperationExecutor(), f.Clock);
            f.Native = new(f.Store, new ServiceOperationExecutor(), admin, f.Subjects);
            var snapshot = await f.Native.ReadManagementSeatsAsync(tenant, license.LicenseId);
            f.Context = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), tenant, Guid.NewGuid(), Guid.NewGuid()) { ResourceId = license.LicenseId, ResourceVersion = snapshot.Version };
            f.Access.Expected = f.Context; f.Target = new(f.Native, f.Access, new ServiceOperationExecutor()); return f;
        }
        public Task<ManagementSeatTransitionResult> Assign(Guid user) => Target.ChangeAsync(ManagementSeatOperations.Assign, Context, Context.ResourceId, user, null, Context.ResourceVersion!);
    }
    internal sealed class Subjects : ILicenseSeatSubjectPolicy
    {
        public Guid Tenant { get; set; }
        public HashSet<Guid> Users { get; } = [];
        public HashSet<Guid> Disabled { get; } = [];
        public Task RequireAsync(Guid tenant, Guid user, string transition, CancellationToken ct = default)
        { if (tenant != Tenant || !Users.Contains(user) || transition == "assign" && Disabled.Contains(user)) throw new LicenseSeatConflictException(); return Task.CompletedTask; }
    }
    internal sealed class Access : IManagementOperationAccessService
    {
        public int ProtocolVersion => 1;
        public ManagementOperationContext Expected { get; set; } = null!;
        public bool Revoked { get; set; }
        public string[] Human { get; set; } = [ManagementSeatOperations.Read, ManagementSeatOperations.Assign, ManagementSeatOperations.Unassign];
        public string[] Machine { get; set; } = [ManagementSeatOperations.Read, ManagementSeatOperations.Assign, ManagementSeatOperations.Unassign];
        private readonly ManagementOperationRegistry registry = new(ManagementSeatServiceExtensions.SeatDescriptors);
        public Task RequireAsync(string operation, ManagementOperationContext context, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (Revoked || context.OrganizationId != Expected.OrganizationId || context.ApplicationId != Expected.ApplicationId || context.RealmId != Expected.RealmId ||
                context.TenantId != Expected.TenantId || context.ActorUserId != Expected.ActorUserId || context.ResourceId != Expected.ResourceId ||
                registry.RequiredFor(operation).Any(x => !Human.Contains(x) || !Machine.Contains(x))) throw new ManagementAccessDeniedException();
            return Task.CompletedTask;
        }
    }
}
