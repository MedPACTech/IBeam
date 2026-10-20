using System.Text.Json;
using IBeam.Identity.Models;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services.Operations;
using IBeam.Management.Identity.Target.Services.Provisioning;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class CustomerProvisioningTests
{
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task NewAndExistingAccountsReceiveOnlyAnInvitationBeforeVerifiedAcceptance(bool existing)
    {
        var f = new ProvisioningFixture();
        if (existing) f.Data.Users.Setup(x => x.FindByEmailAsync(f.Request.Email, It.IsAny<CancellationToken>())).ReturnsAsync(new IdentityUser(Guid.NewGuid(), f.Request.Email, true));
        var first = await f.Service.ProvisionAsync(f.Tenant, f.Request, "provision-intent-01");
        Assert.AreEqual("sent", first.Status); Assert.AreEqual(f.Tenant, first.Operation.ResourceId); Assert.AreEqual("pending", first.Invitation!.Status);
        Assert.HasCount(1, f.Data.InviteRows); Assert.AreEqual(1, f.Data.Sends); Assert.AreEqual(Guid.Empty, f.Data.InviteRows.Values.Single().InvitedByUserId);
        f.Data.Users.Verify(x => x.CreateAsync(It.IsAny<RegisterUserRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        f.Data.Users.Verify(x => x.SetPasswordAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        f.Data.Memberships.Verify(x => x.SetDefaultTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.IsTrue(f.Data.LastRequest!.ApprovedRoleNames.SequenceEqual(new[] { "Member" }));
        Assert.DoesNotContain(f.Request.Email, JsonSerializer.Serialize(f.Journal.Rows.Values));
        Assert.DoesNotContain(f.Data.LastRequest.Token, JsonSerializer.Serialize(f.Journal.Rows.Values));
        Assert.DoesNotContain(f.Request.Email, JsonSerializer.Serialize(f.Data.Operation.Store.Rows.Values));
    }
    [TestMethod]
    public async Task RepeatedAndNewRequestKeysReuseOneInvitationButChangedIntentCannotReuseTheOriginalKey()
    {
        var f = new ProvisioningFixture();
        var first = await f.Service.ProvisionAsync(f.Tenant, f.Request, "provision-intent-01");
        var same = await f.Service.ProvisionAsync(f.Tenant, f.Request, "provision-intent-01");
        var anotherKey = await f.Service.ProvisionAsync(f.Tenant, f.Request, "provision-intent-02");
        Assert.AreEqual(first.Operation, same.Operation); Assert.AreNotEqual(first.Operation.OperationId, anotherKey.Operation.OperationId);
        Assert.AreEqual(first.Invitation!.InviteId, anotherKey.Invitation!.InviteId); Assert.AreEqual(1, f.Data.Sends); Assert.HasCount(1, f.Data.InviteRows);
        Assert.IsNotNull(await f.Journal.GetForOperationAsync(f.Journal.Rows.Values.Single().Scope, f.Tenant, anotherKey.Operation.OperationId));
        await Assert.ThrowsAsync<ManagementIdentityOperationConflictException>(() => f.Service.ProvisionAsync(f.Tenant,
            new() { Email = "changed@example.test", PolicyKey = "default", RoleIds = [f.Role] }, "provision-intent-01"));
        Assert.HasCount(1, f.Data.InviteRows);
    }
    [TestMethod]
    public async Task ANewKeyCannotChangeTheApprovedRolesOnAnExistingRecipientPolicyIntent()
    {
        var f = new ProvisioningFixture(); f.Policy.Policies["default"] = ["Member", "Owner"];
        var owner = Guid.NewGuid(); f.Roles[owner] = "Owner";
        await f.Service.ProvisionAsync(f.Tenant, f.Request, "provision-intent-01");
        await Assert.ThrowsAsync<ManagementIdentityOperationConflictException>(() => f.Service.ProvisionAsync(f.Tenant,
            new() { Email = f.Request.Email, PolicyKey = "default", RoleIds = [owner] }, "provision-intent-02"));
        Assert.AreEqual(1, f.Data.Sends); Assert.HasCount(1, f.Data.InviteRows);
    }
    [TestMethod]
    public async Task CrashAfterNativeInvitationOrJournalPersistenceReconcilesWithoutAnotherInviteOrMessage()
    {
        foreach (var failJournal in new[] { false, true }) {
            var f = new ProvisioningFixture(); f.Data.FailAfterInvite = !failJournal; f.Journal.FailAfterPersisted = failJournal;
            await Assert.ThrowsAsync<ManagementCustomerIdentityUnavailableException>(() => f.Service.ProvisionAsync(f.Tenant, f.Request, "provision-intent-01"));
            var id = f.Data.InviteRows.Keys.Single(); Assert.AreEqual(0, f.Data.Sends);
            f.Data.FailAfterInvite = false; f.Journal.FailAfterPersisted = false;
            var result = await f.Service.ProvisionAsync(f.Tenant, f.Request, "provision-intent-01");
            Assert.AreEqual(id, result.Invitation!.InviteId); Assert.AreEqual("sent", result.Status); Assert.AreEqual(1, f.Data.Sends); Assert.HasCount(1, f.Data.InviteRows);
        }
    }
    [TestMethod]
    public async Task UncertainDeliveryAndLostAcknowledgementsDoNotAutomaticallyResend()
    {
        var f = new ProvisioningFixture(); f.Data.FailDelivery = true;
        var first = await f.Service.ProvisionAsync(f.Tenant, f.Request, "provision-intent-01"); Assert.AreEqual("unknown", first.Status);
        f.Data.FailDelivery = false;
        var repeat = await f.Service.ProvisionAsync(f.Tenant, f.Request, "provision-intent-01"); Assert.AreEqual("unknown", repeat.Status); Assert.AreEqual(1, f.Data.Sends);
        var acknowledged = new ProvisioningFixture(); acknowledged.Journal.FailSent = true;
        await Assert.ThrowsAsync<ManagementCustomerIdentityUnavailableException>(() => acknowledged.Service.ProvisionAsync(acknowledged.Tenant, acknowledged.Request, "provision-intent-01"));
        acknowledged.Journal.FailSent = false; acknowledged.Data.Operation.Clock.Now = acknowledged.Data.Operation.Clock.Now.AddMinutes(3);
        var recovered = await acknowledged.Service.ProvisionAsync(acknowledged.Tenant, acknowledged.Request, "provision-intent-01");
        Assert.AreEqual("unknown", recovered.Status); Assert.AreEqual(1, acknowledged.Data.Sends);
    }
    [TestMethod]
    [DataRow(true)] [DataRow(false)]
    public async Task ActiveAndDisabledMembershipsAreNeverSilentlyRelinkedOrReenabled(bool active)
    {
        var f = new ProvisioningFixture(); var user = new IdentityUser(Guid.NewGuid(), f.Request.Email, true);
        f.Data.Users.Setup(x => x.FindByEmailAsync(f.Request.Email, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        f.Data.Memberships.Setup(x => x.GetTenantForUserAsync(user.UserId, f.Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(new TenantInfo(f.Tenant, "Private", ["Owner"], active));
        await Assert.ThrowsAsync<ManagementIdentityOperationConflictException>(() => f.Service.ProvisionAsync(f.Tenant, f.Request, "provision-intent-01"));
        Assert.HasCount(0, f.Data.InviteRows); Assert.AreEqual(0, f.Data.Sends);
    }
    [TestMethod]
    public async Task DisabledTenantMissingCompositeGrantAndCrossTenantRolesCannotReachInvitations()
    {
        var f = new ProvisioningFixture(); f.Allowed = false;
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.ProvisionAsync(f.Tenant, f.Request, "provision-intent-01"));
        Assert.AreEqual(0, f.RoleReads); f.Allowed = true;
        f.Data.Tenants.Store.Rows[f.Tenant] = f.Data.Tenants.Store.Rows[f.Tenant] with { Tenant = new IdentityTenant(f.Tenant, "Private", Status: "Disabled") };
        await Assert.ThrowsAsync<ManagementIdentityOperationConflictException>(() => f.Service.ProvisionAsync(f.Tenant, f.Request, "provision-intent-01"));
        Assert.HasCount(0, f.Data.InviteRows);
        var other = new ProvisioningFixture();
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => other.Service.ProvisionAsync(other.Tenant,
            new() { Email = other.Request.Email, PolicyKey = "default", RoleIds = [Guid.NewGuid()] }, "provision-intent-01"));
        Assert.HasCount(0, other.Data.InviteRows);
    }
    [TestMethod]
    public async Task CatalogVisibilityDoesNotGrantAssignmentAndOnlyHostApprovedTargetRolesAreListed()
    {
        var f = new ProvisioningFixture(); f.Data.Operation.Actor = f.Data.Operation.Actor with { Operation = ManagementCustomerIdentityOperations.RolesRead };
        var roles = await f.Catalog.ListAsync(f.Tenant); Assert.HasCount(1, roles); Assert.AreEqual("Member", roles[0].Name);
        Assert.IsTrue(roles[0].PolicyKeys.SequenceEqual(new[] { "default" }));
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.ProvisionAsync(f.Tenant, f.Request, "provision-intent-01"));
        Assert.HasCount(0, f.Data.InviteRows);
        Assert.IsFalse(new ManagementCustomerProvisioningOptions { Policies = new() { ["default"] = ["SupportAdmin"] } }.IsValid());
        Assert.IsFalse(new ManagementCustomerProvisioningOptions { Policies = new() { ["default"] = ["*"] } }.IsValid());
    }
}

internal sealed class ProvisioningFixture : IManagementCustomerIdentityTargetAccessService, IManagementAssignableRoleReadRepository, IManagementCustomerInvitationReadRepository
{
    public OnboardingFixture Data { get; }
    public ProvisioningJournal Journal { get; } = new();
    public Guid Tenant { get; } = Guid.NewGuid();
    public Guid Role => Data.RoleId;
    public bool Allowed { get; set; } = true;
    public int RoleReads { get; private set; }
    public Dictionary<Guid, string> Roles { get; } = [];
    public ManagementCustomerProvisioningOptions Policy { get; } = new() { Policies = new() { ["default"] = ["Member"] } };
    public ProvisionManagementCustomerUser Request => new() { Email = "customer@example.test", PolicyKey = "default", RoleIds = [Role] };
    public IManagementCustomerProvisioningService Service { get; }
    public IManagementAssignableRoleCatalogService Catalog { get; }
    public ProvisioningFixture(OnboardingFixture? data = null)
    {
        Data = data ?? new();
        Data.Operation.Actor = Data.Operation.Actor with { Operation = ManagementCustomerIdentityOperations.UsersProvision };
        Roles[Role] = "Member";
        Data.Tenants.Store.Rows[Tenant] = new(new IdentityTenant(Tenant, "Private"), "v1", null);
        var executor = new ServiceOperationExecutor();
        Catalog = new ManagementAssignableRoleCatalogService(this, Data.Tenants.Store, this, Options.Create(Policy), Options.Create(Data.Operation.Options), executor);
        var receipts = new ManagementIdentityOperationService(Data.Operation.Store, Data.Operation, this, Options.Create(Data.Operation.Options), Data.Operation.Clock, executor);
        Service = new ManagementCustomerProvisioningService(Journal, receipts, Catalog, Data.External, Data.InviteStore, this,
            Data.Users.Object, Data.Memberships.Object, Data.Tenants.Store, this, Options.Create(Data.Operation.Options), Data.Operation.Clock, executor);
    }
    public Task<ManagementDelegatedActor> RequireAsync(string operation, CancellationToken ct = default)
        => Allowed && ManagementCustomerIdentityPermissionPolicy.RequiredFor(Data.Operation.Actor.Operation).Contains(operation)
            ? Task.FromResult(Data.Operation.Actor with { Operation = operation }) : Task.FromException<ManagementDelegatedActor>(new ManagementAccessDeniedException());
    public Task<IReadOnlyList<ManagementAssignableRoleReadEntity>> ReadAsync(Guid tenantId, IReadOnlyList<string> names, CancellationToken ct = default)
    { RoleReads++; return Task.FromResult<IReadOnlyList<ManagementAssignableRoleReadEntity>>(Roles.Where(x => names.Contains(x.Value)).Select(x => new ManagementAssignableRoleReadEntity(tenantId, x.Key, x.Value, true)).ToArray()); }
    public string SourceId => "provisioning-fixture-invites";
    public Task<ManagementCustomerReadPage<ManagementCustomerInvitationReadEntity>> ReadPageAsync(Guid tenantId, int size, string? cursor, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ManagementCustomerInvitationReadEntity?> FindAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        if (!Data.InviteRows.TryGetValue(id, out var row) || row.TenantId != tenantId) return Task.FromResult<ManagementCustomerInvitationReadEntity?>(null);
        return Task.FromResult<ManagementCustomerInvitationReadEntity?>(new(tenantId, id, "email", row.NormalizedDestination, null, null, row.Status,
            string.Join(',', row.RoleIds ?? []), "[]", row.CreatedUtc, row.ExpiresUtc, row.SentUtc, row.RedeemedUtc, row.RevokedUtc, "fixture-invite-version"));
    }
}
internal sealed class ProvisioningJournal : IManagementCustomerProvisioningRepository
{
    public Dictionary<(ManagementTenantAdministrationScope, Guid, Guid), ManagementCustomerProvisioningEntity> Rows { get; } = [];
    private readonly Dictionary<(ManagementTenantAdministrationScope, Guid, Guid), Guid> links = [];
    public bool FailSent { get; set; }
    public bool FailAfterPersisted { get; set; }
    public Task LinkOperationAsync(ManagementTenantAdministrationScope scope, Guid tenant, Guid operation, Guid invite, CancellationToken ct = default)
    { lock (Rows) { var key = (scope, tenant, operation); if (links.TryGetValue(key, out var old) && old != invite) throw new ManagementIdentityOperationConflictException(); links[key] = invite; return Task.CompletedTask; } }
    public Task<ManagementCustomerProvisioningEntity?> GetForOperationAsync(ManagementTenantAdministrationScope scope, Guid tenant, Guid operation, CancellationToken ct = default)
    { lock (Rows) return Task.FromResult(links.TryGetValue((scope, tenant, operation), out var id) ? Rows.GetValueOrDefault((scope, tenant, id)) : null); }
    public Task<ManagementCustomerProvisioningEntity?> GetAsync(ManagementTenantAdministrationScope scope, Guid tenant, Guid id, CancellationToken ct = default)
    { lock (Rows) return Task.FromResult(Rows.GetValueOrDefault((scope, tenant, id))); }
    public Task<bool> TryCreateAsync(ManagementCustomerProvisioningEntity row, CancellationToken ct = default)
    { lock (Rows) return Task.FromResult(Rows.TryAdd((row.Scope, row.TenantId, row.InviteId), row with { Version = Guid.NewGuid().ToString() })); }
    public Task<ManagementCustomerProvisioningEntity> ReplaceAsync(ManagementCustomerProvisioningEntity row, string version, CancellationToken ct = default)
    {
        lock (Rows) {
            if (FailSent && row.DeliveryStatus == "sent") throw new Exception("PROVIDER-SECRET-CANARY");
            var key = (row.Scope, row.TenantId, row.InviteId);
            if (!Rows.TryGetValue(key, out var old) || old.Version != version) throw new ManagementIdentityOperationConflictException();
            Rows[key] = row = row with { Version = Guid.NewGuid().ToString() };
            if (FailAfterPersisted && row.Stage == "invitation-persisted" && row.DeliveryStatus == "pending") throw new Exception("PROVIDER-SECRET-CANARY");
            return Task.FromResult(row);
        }
    }
}
