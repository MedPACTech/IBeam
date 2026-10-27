using System.Text.Json;
using IBeam.Identity.Models;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class TenantAdministrationTests
{
    [TestMethod]
    public async Task CreationReservesOneTenantAndCallsIdentityWithoutCustomerMembershipOrDefaultContext()
    {
        var f = new TenantAdministrationFixture(); var request = new CreateManagementCustomerTenant { Name = " Enterprise ", ExternalReference = "CRM:123" };
        var created = await f.Service.CreateAsync(request, "create-intent-1234");
        var repeat = await f.Service.CreateAsync(request, "create-intent-1234");
        Assert.AreEqual(created.Operation, repeat.Operation); Assert.AreEqual("Enterprise", created.Tenant!.Name);
        Assert.AreEqual("support-provisioned", created.Source); Assert.AreEqual("CRM:123", created.ExternalReference);
        Assert.AreEqual(1, f.Store.Creates); Assert.AreEqual(1, f.Hooks.Created); Assert.IsNotNull(created.Tenant.Version);
        Assert.IsNull(f.Hooks.Context!.AuthUserId); Assert.IsTrue(f.Hooks.Context.RedactFailureDetails);
        Assert.AreEqual("external-management", f.Hooks.Context.Metadata!["origin"]);
        Assert.AreEqual(f.Operations.Actor.UserId.ToString("D"), f.Hooks.Context.Metadata["support_user_id"]);
        Assert.DoesNotContain("Enterprise", JsonSerializer.Serialize(f.Operations.Store.Rows.Values));
        Assert.DoesNotContain("CRM:123", JsonSerializer.Serialize(f.Operations.Events));
    }
    [TestMethod]
    public async Task ExternalReferencesAreUniqueCaseInsensitivelyEvenAcrossSupportOrganizationsInOneTargetRealm()
    {
        var f = new TenantAdministrationFixture();
        await f.Service.CreateAsync(new() { Name = "One", ExternalReference = "CRM:123" }, "create-intent-1234");
        f.Operations.Actor = f.Operations.Actor with { OrganizationId = Guid.NewGuid() };
        await Assert.ThrowsAsync<ManagementIdentityOperationConflictException>(() => f.Service.CreateAsync(new() { Name = "Two", ExternalReference = "crm:123" }, "another-intent-1234"));
        Assert.HasCount(1, f.Store.Rows); Assert.HasCount(1, f.Metadata.Rows);
    }
    [TestMethod]
    public async Task FailedConsumerExtensionResumesTheExistingTrackedTenantWithoutCreatingOrRollingItBack()
    {
        var f = new TenantAdministrationFixture(); f.Hooks.FailCreated = true;
        var request = new CreateManagementCustomerTenant { Name = "Enterprise" };
        var failure = await Assert.ThrowsAsync<ManagementCustomerIdentityUnavailableException>(() => f.Service.CreateAsync(request, "create-intent-1234"));
        Assert.DoesNotContain("CONSUMER-PROFILE", failure.ToString());
        var tenant = f.Store.Rows.Values.Single().Tenant; Assert.AreEqual("creating", f.Metadata.Rows.Values.Single().Phase);
        Assert.AreEqual("uncertain", f.Operations.Store.Rows.Values.Single().Status);
        f.Hooks.FailCreated = false;
        var completed = await f.Service.CreateAsync(request, "create-intent-1234");
        Assert.AreEqual(tenant.TenantId, completed.Tenant!.TenantId); Assert.AreEqual(1, f.Store.Creates); Assert.AreEqual(2, f.Hooks.Created);
        Assert.AreEqual("ready", f.Metadata.Rows.Values.Single().Phase); Assert.AreEqual("succeeded", completed.Operation.Status);
    }
    [TestMethod]
    public async Task RenamesRequireExactVersionsAndRetriesDoNotOverwriteOrRepeatTheIdentityWrite()
    {
        var f = new TenantAdministrationFixture(); var created = await f.Service.CreateAsync(new() { Name = "Original" }, "create-intent-1234");
        f.UpdateAuthority(); var version = created.Tenant!.Version!; var id = created.Tenant.TenantId;
        var renamed = await f.Service.RenameAsync(id, new() { Name = "New Name" }, version, "rename-intent-1234");
        var retried = await f.Service.RenameAsync(id, new() { Name = "New Name" }, version, "rename-intent-1234");
        Assert.AreEqual(renamed.Operation, retried.Operation); Assert.AreEqual("New Name", renamed.Tenant!.Name);
        Assert.AreNotEqual(version, renamed.Tenant.Version); Assert.AreEqual(1, f.Store.Renames);
        await Assert.ThrowsAsync<ManagementCustomerIdentityPreconditionException>(() => f.Service.RenameAsync(id, new() { Name = "Stale" }, version, "stale-intent-1234"));
        Assert.AreEqual("New Name", f.Store.Rows[id].Tenant.Name);
    }
    [TestMethod]
    public async Task RenameExtensionFailureRecoversByOperationMarkerWithoutRepeatingTheConditionalWrite()
    {
        var f = new TenantAdministrationFixture(); var created = await f.Service.CreateAsync(new() { Name = "Original" }, "create-intent-1234");
        f.UpdateAuthority(); f.Hooks.FailUpdated = true;
        await Assert.ThrowsAsync<ManagementCustomerIdentityUnavailableException>(() => f.Service.RenameAsync(created.Tenant!.TenantId,
            new() { Name = "Renamed" }, created.Tenant.Version!, "rename-intent-1234"));
        Assert.AreEqual("renaming", f.Metadata.Rows.Values.Single().Phase); Assert.AreEqual(1, f.Store.Renames);
        f.Hooks.FailUpdated = false;
        var done = await f.Service.RenameAsync(created.Tenant!.TenantId, new() { Name = "Renamed" }, created.Tenant.Version!, "rename-intent-1234");
        Assert.AreEqual("succeeded", done.Operation.Status); Assert.AreEqual(1, f.Store.Renames); Assert.AreEqual(2, f.Hooks.Updated);
        Assert.IsNull(f.Hooks.Previous); Assert.IsNull(f.Hooks.Context!.AuthUserId);
    }
    [TestMethod]
    public async Task LegacyProvenanceIsUnknownAndUnsupportedConditionalStoresDoNotAllowUpdates()
    {
        var f = new TenantAdministrationFixture(); var tenant = new IdentityTenant(Guid.NewGuid(), "Legacy"); await f.Store.CreateAsync(tenant);
        Assert.IsNull((await f.Identity.FindAsync(tenant.TenantId))!.LastOperationId); Assert.HasCount(0, f.Metadata.Rows);
        f.UpdateAuthority(); var result = await f.Service.RenameAsync(tenant.TenantId, new() { Name = "Updated" }, f.Store.Rows[tenant.TenantId].Version!, "rename-intent-1234");
        Assert.AreEqual("unknown", result.Source); Assert.IsNull(result.ExternalReference);
        var legacy = new TenantAdministrationFixture(conditional: false); Assert.IsFalse(legacy.Identity.SupportsConditionalUpdates);
        var created = await legacy.Service.CreateAsync(new() { Name = "Legacy provider" }, "create-intent-1234"); legacy.UpdateAuthority();
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => legacy.Service.RenameAsync(created.Tenant!.TenantId, new() { Name = "Forbidden" }, "some-version", "rename-intent-1234"));
        Assert.AreEqual(0, legacy.Store.Renames);
    }
    [TestMethod]
    [DataRow("")] [DataRow(" ")] [DataRow("unsafe\nname")]
    public async Task InvalidNamesNeverReserveOrCreateATenant(string name)
    {
        var f = new TenantAdministrationFixture(); await Assert.ThrowsAsync<ManagementCustomerIdentityInvalidRequestException>(() => f.Service.CreateAsync(new() { Name = name }, "create-intent-1234"));
        Assert.HasCount(0, f.Operations.Store.Rows); Assert.AreEqual(0, f.Store.Creates);
    }
    [TestMethod]
    [DataRow("")] [DataRow("*")]
    public async Task MissingAndWildcardVersionsNeverMutateATenant(string version)
    {
        var f = new TenantAdministrationFixture(); f.UpdateAuthority();
        await Assert.ThrowsAsync<ManagementCustomerIdentityInvalidRequestException>(() => f.Service.RenameAsync(Guid.NewGuid(), new() { Name = "Rename" }, version, "rename-intent-1234"));
        Assert.HasCount(0, f.Operations.Store.Rows); Assert.AreEqual(0, f.Store.Renames);
    }
    [TestMethod]
    public async Task InvalidExternalReferencesAndDeniedDirectCallsCannotReachCustomerStorage()
    {
        var f = new TenantAdministrationFixture();
        await Assert.ThrowsAsync<ManagementCustomerIdentityInvalidRequestException>(() => f.Service.CreateAsync(new() { Name = "Enterprise", ExternalReference = "email@example.test" }, "create-intent-1234"));
        f.Operations.Allowed = false;
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Service.CreateAsync(new() { Name = "Enterprise" }, "create-intent-1234"));
        Assert.AreEqual(0, f.Store.Creates); Assert.HasCount(0, f.Metadata.Rows);
    }
}
