using IBeam.Identity.Exceptions;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
namespace IBeam.Tests.Management.CustomerIdentity;
[TestClass]
public sealed class TenantStatusTests
{
    [TestMethod]
    public async Task ExactVersionStatusReplayDoesNotRepeatCustomerHooksAndChangedIntentConflicts()
    {
        var f = new TenantAdministrationFixture(); var created = await f.Service.CreateAsync(new() { Name = "Private company" }, "status-create-intent");
        f.Operations.Actor = f.Operations.Actor with { Operation = ManagementCustomerIdentityOperations.TenantsStatus };
        var id = created.Tenant!.TenantId; var original = created.Tenant.Version!;
        var disabled = await f.Service.SetStatusAsync(id, false, original, "status-disable-intent");
        Assert.AreEqual("Disabled", disabled.Tenant!.Status); Assert.AreEqual(1, f.Store.StatusWrites); Assert.AreEqual(1, f.Hooks.Deactivated);
        var repeat = await f.Service.SetStatusAsync(id, false, original, "status-disable-intent"); Assert.AreEqual(disabled.Tenant.Version, repeat.Tenant!.Version); Assert.AreEqual(1, f.Hooks.Deactivated);
        await Assert.ThrowsAsync<ManagementIdentityOperationConflictException>(() => f.Service.SetStatusAsync(id, true, original, "status-disable-intent"));
        await Assert.ThrowsAsync<ManagementCustomerIdentityPreconditionException>(() => f.Service.SetStatusAsync(id, true, original, "status-stale-intent"));
        var activated = await f.Service.SetStatusAsync(id, true, disabled.Tenant.Version!, "status-activate-intent"); Assert.AreEqual("Active", activated.Tenant!.Status);
        Assert.AreEqual(2, f.Store.StatusWrites); Assert.AreEqual(1, f.Hooks.Activated); Assert.IsNull(f.Hooks.Context!.AuthUserId); Assert.IsTrue(f.Hooks.Context.RequireLifecycleDelivery);
        Assert.AreEqual("activated", f.Hooks.Context.Operation);
    }
    [TestMethod]
    public async Task FailedRequiredStatusHookResumesTheSameOwnedIntentWithoutAnotherNativeWrite()
    {
        var f = new TenantAdministrationFixture(); var created = await f.Service.CreateAsync(new() { Name = "Private company" }, "status-create-intent");
        f.Operations.Actor = f.Operations.Actor with { Operation = ManagementCustomerIdentityOperations.TenantsStatus }; f.Hooks.FailStatus = true;
        var id = created.Tenant!.TenantId;
        await Assert.ThrowsAsync<ManagementCustomerIdentityUnavailableException>(() => f.Service.SetStatusAsync(id, false, created.Tenant.Version!, "status-recover-intent"));
        var applied = f.Store.Rows[id]; Assert.AreEqual("Disabled", applied.Tenant.Status); Assert.AreEqual("status-changing", f.Metadata.Rows.Values.Single().Phase);
        f.Hooks.FailStatus = false;
        var result = await f.Service.SetStatusAsync(id, false, created.Tenant.Version!, "status-recover-intent"); Assert.AreEqual(applied.Version, result.Tenant!.Version);
        Assert.AreEqual(1, f.Store.StatusWrites); Assert.AreEqual(2, f.Hooks.Deactivated); Assert.AreEqual("ready", f.Metadata.Rows.Values.Single().Phase);
    }
    [TestMethod]
    public async Task AConcurrentNativeWriteCannotLeaveAnUnappliedMetadataReservationPermanentlyLocked()
    {
        var f = new TenantAdministrationFixture(); var created = await f.Service.CreateAsync(new() { Name = "Private company" }, "status-create-intent");
        f.Operations.Actor = f.Operations.Actor with { Operation = ManagementCustomerIdentityOperations.TenantsStatus }; var id = created.Tenant!.TenantId;
        f.Store.BeforeStatus = async () => { await f.Store.UpdateAsync(f.Store.Rows[id].Tenant with { Name = "Concurrent name" }); };
        await Assert.ThrowsAsync<ManagementCustomerIdentityPreconditionException>(() => f.Service.SetStatusAsync(id, false, created.Tenant.Version!, "status-race-intent"));
        Assert.AreEqual("ready", f.Metadata.Rows.Values.Single().Phase); Assert.AreEqual(0, f.Store.StatusWrites);
        var result = await f.Service.SetStatusAsync(id, false, f.Store.Rows[id].Version!, "status-fresh-intent"); Assert.AreEqual("Disabled", result.Tenant!.Status);
    }
    [TestMethod]
    public async Task ReadCreateAndRenameAuthorityCannotChangeStatusOrBypassLiveRevocation()
    {
        var f = new TenantAdministrationFixture(); var created = await f.Service.CreateAsync(new() { Name = "Private company" }, "status-create-intent");
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.SetStatusAsync(created.Tenant!.TenantId, false, created.Tenant.Version!, "status-denied-intent"));
        f.Operations.Actor = f.Operations.Actor with { Operation = ManagementCustomerIdentityOperations.TenantsStatus }; f.Operations.Allowed = false;
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.SetStatusAsync(created.Tenant!.TenantId, false, created.Tenant.Version!, "status-revoked-intent"));
        Assert.AreEqual(0, f.Store.StatusWrites);
    }
}
