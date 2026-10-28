using System.Text.Json;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Repositories.AzureTable.Stores;
using IBeam.Identity.Services.Tenants;
using IBeam.Management;
using IBeam.Management.Operations;
using IBeam.Management.SyntheticConsumer;
using IBeam.Management.TenantData;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Tests.Management.TenantData;

[TestClass]
[TestCategory("StorageIntegration")]
public sealed class SyntheticConsumerPurgeTests
{
    [TestMethod]
    public async Task AppRowsAndActualFileBytesArePurgedWithNativeIdentityWhileAnotherTenantRemainsUsable()
    {
        await using var f = await IdentityPurgeFixture.Create(); using var app = SyntheticTenantPurger.CreateNew(f.Execution); f.UseConsumer(app);
        using var scope = f.Provider.CreateScope(); var p = scope.ServiceProvider;
        var tenants = p.GetRequiredService<AzureTableIdentityTenantStore>(); var members = p.GetRequiredService<AzureTableTenantMembershipStore>();
        var roles = p.GetRequiredService<AzureTableTenantRoleStore>(); var access = new IdentityTenantAccessValidator(members, tenants, new ServiceOperationExecutor());
        var original = await access.RequireAsync(f.User, f.Tenant, null); var other = await access.RequireAsync(f.User, f.OtherTenant, null);
        app.CustomerWriteOrAccess(); Assert.IsTrue(app.DataExists); await f.Confirm(); Assert.IsTrue(app.DataExists);
        await f.Run(); Assert.Throws<ManagementAccessDeniedException>(app.CustomerWriteOrAccess);
        await f.Complete(); var result = await f.Status(); Assert.AreEqual("succeeded", result.Job!.State);
        Assert.IsFalse(app.DataExists); Assert.AreEqual(2, app.CompletedSteps); Assert.IsTrue(app.BackupExists);
        Assert.IsNull(await tenants.FindByIdAsync(f.Tenant)); Assert.IsNull(await members.FindStateAsync(f.Tenant, f.User));
        Assert.IsTrue((await roles.GetRolesForUserAsync(f.OtherTenant, f.User)).Any(x => x.Name == "Owner"));
        await Assert.ThrowsAsync<IdentityUnauthorizedException>(() => access.RequireAsync(f.User, f.Tenant, original.Version));
        Assert.AreEqual(other.Version, (await access.RequireAsync(f.User, f.OtherTenant, other.Version)).Version);
        Assert.AreEqual(f.OtherTenant, await members.GetDefaultTenantIdAsync(f.User));
        foreach (var table in new[] { "AspNetUsers", "OtpChallenges", "AuthSessions" })
            Assert.IsTrue((await f.Table(table).GetEntityIfExistsAsync<Azure.Data.Tables.TableEntity>("GLOBAL", f.User.ToString("D"))).HasValue);
        foreach (var key in new[] { "app-datasets", "audio-files" }) Assert.AreEqual("deleted", result.Job.Coverage.Single(x => x.Key == key).Disposition);
        foreach (var key in new[] { "backups", "app-purge-controls", "identity-purge-tombstones" }) Assert.AreEqual("retained", result.Job.Coverage.Single(x => x.Key == key).Disposition);
        foreach (var key in new[] { "external-systems", "global-accounts" }) Assert.AreEqual("excluded", result.Job.Coverage.Single(x => x.Key == key).Disposition);
        var restarted = app.Reopen(); Assert.Throws<ManagementAccessDeniedException>(restarted.CustomerWriteOrAccess);
        Assert.DoesNotContain("PRIVATE-", JsonSerializer.Serialize(result) + JsonSerializer.Serialize(f.Audits));
    }
    [TestMethod]
    [DataRow("app-fence")] [DataRow("app-datasets")] [DataRow("audio-files")] [DataRow("verification")]
    public async Task EveryConsumerPhasePausesOnLostReplyAndReopensTheSameJobWithoutRevivingAccess(string phase)
    {
        await using var f = await IdentityPurgeFixture.Create(); using var app = SyntheticTenantPurger.CreateNew(f.Execution); f.UseConsumer(app);
        var injected = false; app.AfterCommitForTesting = committed => { if (!injected && committed == phase) { injected = true; throw new IOException("PRIVATE-SYNTHETIC-LOST-REPLY"); } };
        await f.Confirm();
        for (var i = 0; i < 160 && (await f.Status()).Job!.State != "unknown"; i++) await f.Run();
        Assert.IsTrue(injected); Assert.AreEqual("unknown", (await f.Status()).Job!.State); Assert.IsFalse(f.Audits.Any(x => x.Succeeded));
        var steps = app.CompletedSteps; await f.Run(); Assert.AreEqual(steps, app.CompletedSteps); Assert.AreEqual("unknown", (await f.Status()).Job!.State);
        Assert.Throws<ManagementAccessDeniedException>(app.CustomerWriteOrAccess);
        // A new app adapter instance has no in-memory receipt or fault state. The standard coordinator reads its durable original journal.
        var restarted = app.Reopen(); f.UseConsumer(restarted);
        await f.Run(reconcile: true); await f.Complete(); Assert.AreEqual("succeeded", (await f.Status()).Job!.State);
        Assert.AreEqual(2, restarted.CompletedSteps); Assert.IsFalse(restarted.DataExists); Assert.IsTrue(restarted.BackupExists);
        Assert.Throws<ManagementAccessDeniedException>(restarted.CustomerWriteOrAccess);
        Assert.DoesNotContain("PRIVATE-", JsonSerializer.Serialize(await f.Status()) + JsonSerializer.Serialize(f.Audits));
    }
    [TestMethod]
    [DataRow("app")] [DataRow("realm")] [DataRow("tenant")] [DataRow("person")] [DataRow("production")]
    public async Task SyntheticConsumerRefusesForeignBindingsAndProductionWithoutTouchingItsFiles(string change)
    {
        await using var f = await IdentityPurgeFixture.Create(); using var app = SyntheticTenantPurger.CreateNew(f.Execution);
        var e = change switch {
            "app" => f.Execution with { Authority = f.Execution.Authority with { ApplicationId = Guid.NewGuid() } },
            "realm" => f.Execution with { Authority = f.Execution.Authority with { RealmId = Guid.NewGuid() } },
            "tenant" => f.Execution with { Authority = f.Execution.Authority with { TenantId = Guid.NewGuid(), ResourceId = Guid.NewGuid() } },
            "person" => f.Execution with { Authority = f.Execution.Authority with { ActorUserId = Guid.NewGuid() } },
            _ => f.Execution with { Environment = "Production" }
        };
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => app.PreviewAsync(e));
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => app.PurgeOrReconcileAsync(e));
        Assert.IsTrue(app.DataExists); Assert.AreEqual(0, app.CompletedSteps);
        if (change == "production") Assert.Throws<ManagementAccessDeniedException>(() => SyntheticTenantPurger.CreateNew(e));
    }
    [TestMethod]
    public async Task ConsumerRequiresTheCurrentTrustedJournalLeaseAndAcquiredFence()
    {
        await using var f = await IdentityPurgeFixture.Create(); using var app = SyntheticTenantPurger.CreateNew(f.Execution);
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => app.PurgeOrReconcileAsync(f.Execution));
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => app.FenceOrReconcileAsync(f.Execution, f.Execution.Authority.ResourceVersion!));
        Assert.IsTrue(app.DataExists); app.CustomerWriteOrAccess(); Assert.AreEqual(0, app.CompletedSteps);
    }
    [TestMethod]
    [DataRow("Tenants")] [DataRow("TenantUsers")] [DataRow("UserTenants")] [DataRow("Roles")] [DataRow("TenantInvites")]
    [DataRow("ApiCredentials")] [DataRow("AgentUsers")] [DataRow("AgentUserCredentials")] [DataRow("OAuthConsents")] [DataRow("AccessCatalogOverrides")]
    public async Task EveryNativeCleanupFamilyCanLoseItsCommitReplyAndRecoverWithoutRepeatingConsumerEffects(string family)
    {
        await using var f = await IdentityPurgeFixture.Create(); using var app = SyntheticTenantPurger.CreateNew(f.Execution); f.UseConsumer(app);
        await f.Confirm(); f.Storage.LoseDeleteTable = f.Table(family).Name;
        for (var i = 0; i < 160 && (await f.Status()).Job!.State != "unknown"; i++) await f.Run();
        Assert.AreEqual("unknown", (await f.Status()).Job!.State); Assert.IsFalse(f.Audits.Any(x => x.Succeeded));
        Assert.AreEqual(2, app.CompletedSteps); Assert.Throws<ManagementAccessDeniedException>(app.CustomerWriteOrAccess);
        await f.Run(); Assert.AreEqual("unknown", (await f.Status()).Job!.State);
        f.UseConsumer(app.Reopen()); await f.Run(reconcile: true); await f.Complete();
        Assert.AreEqual(2, app.CompletedSteps); Assert.IsFalse(app.DataExists); Assert.IsTrue(app.BackupExists);
        Assert.AreEqual("succeeded", (await f.Status()).Job!.State);
        Assert.IsTrue((await new AzureTableIdentityTenantStore(f.Storage, Microsoft.Extensions.Options.Options.Create(f.NativeOptions)).FindByIdAsync(f.OtherTenant)) is not null);
    }
}
