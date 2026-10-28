using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Repositories.AzureTable.Stores;
using IBeam.Identity.Services.Tenants;
using IBeam.Management;
using IBeam.Management.Operations;
using IBeam.Management.TenantData;
using IBeam.Management.TenantData.Identity;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;

namespace IBeam.Tests.Management.TenantData;

[TestClass]
[TestCategory("StorageIntegration")]
public sealed class IdentityPurgeTests
{
    [TestMethod]
    public async Task CompleteNativeCleanupPreservesSharedGlobalIdentityAndAnotherTenantsOwnerAndAccess()
    {
        await using var f = await IdentityPurgeFixture.Create();
        using var before = f.Provider.CreateScope(); var p = before.ServiceProvider;
        var members = p.GetRequiredService<AzureTableTenantMembershipStore>(); var roles = p.GetRequiredService<AzureTableTenantRoleStore>(); var tenants = p.GetRequiredService<AzureTableIdentityTenantStore>();
        var access = new IdentityTenantAccessValidator(members, tenants, new ServiceOperationExecutor());
        var a = await access.RequireAsync(f.User, f.Tenant, null); var b = await access.RequireAsync(f.User, f.OtherTenant, null);
        var snapshots = new Dictionary<string, string>();
        foreach (var table in new[] { "AspNetUsers", "OtpChallenges", "AuthSessions" }) snapshots[table] = (await f.Table(table).GetEntityAsync<TableEntity>("GLOBAL", f.User.ToString("D"))).Value.ETag.ToString();
        await f.Confirm(); await f.Complete(); var result = await f.Status();
        Assert.AreEqual("succeeded", result.Job!.State); Assert.IsNull(await tenants.FindByIdAsync(f.Tenant));
        Assert.AreEqual(1, f.App.Effects.Count); Assert.DoesNotContain("PRIVATE-", JsonSerializer.Serialize(result) + JsonSerializer.Serialize(f.Audits));
        Assert.AreEqual("retained", result.Job.Coverage.Single(x => x.Key == "identity-purge-tombstones").Disposition);
        Assert.AreEqual("excluded", result.Job.Coverage.Single(x => x.Key == "global-accounts").Disposition);
        await Assert.ThrowsAsync<IdentityUnauthorizedException>(() => access.RequireAsync(f.User, f.Tenant, a.Version));
        Assert.AreEqual(b.Version, (await access.RequireAsync(f.User, f.OtherTenant, b.Version)).Version);
        Assert.AreEqual(f.OtherTenant, await members.GetDefaultTenantIdAsync(f.User));
        var claims = new IdentityTenantAccessClaimsEnricher(access);
        await Assert.ThrowsAsync<IdentityUnauthorizedException>(() => claims.EnrichAsync(new(f.User, f.Tenant, [new(IdentityTenantAccessClaims.Version, a.Version)])));
        Assert.IsTrue((await claims.EnrichAsync(new(f.User, f.OtherTenant, [new(IdentityTenantAccessClaims.Version, b.Version)]))).Count > 0);
        foreach (var table in snapshots.Keys) Assert.AreEqual(snapshots[table], (await f.Table(table).GetEntityAsync<TableEntity>("GLOBAL", f.User.ToString("D"))).Value.ETag.ToString());
        Assert.IsTrue((await roles.GetRolesForUserAsync(f.OtherTenant, f.User)).Any(x => x.Name == "Owner"));
        var protection = new IdentityTenantOwnershipProtection(members, roles, Options.Create(new IdentityTenantOwnershipOptions { Enabled = true, OwnerRoleNames = ["Owner"] }), new ServiceOperationExecutor());
        var admin = new IdentityTenantMembershipAdministrationService(members, roles, tenants, new ServiceOperationExecutor(), protection);
        var state = (await members.FindStateAsync(f.OtherTenant, f.User))!; var administrator = (await roles.GetRolesAsync(f.OtherTenant)).Single(x => x.Name == "Administrator");
        await Assert.ThrowsAsync<IdentityValidationException>(() => admin.SetEnabledAsync(f.OtherTenant, f.User, false, state.Version, Guid.NewGuid(), [administrator.RoleId]));
        Assert.IsTrue((await members.FindStateAsync(f.OtherTenant, f.User))!.IsEnabled);
        var native = p.GetRequiredService<IIdentityTenantPurgeService>(); Assert.IsTrue(await native.HasPurgeMarkerAsync(f.NativeScope()));
        Assert.IsFalse(await native.HasPurgeMarkerAsync(f.NativeScope() with { ApplicationId = Guid.NewGuid() }));
    }
    [TestMethod]
    public async Task UnscopedAccountConsentIsPreservedWithGlobalIdentityData()
    {
        await using var f = await IdentityPurgeFixture.Create();
        var store = new AzureTableOAuthConsentStore(f.Storage, Options.Create(f.NativeOptions));
        await store.UpsertAsync(new(Guid.NewGuid(), f.User, Guid.Empty, "global-client", "resource", ["PRIVATE-GLOBAL-SCOPE"], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        await f.Confirm(); await f.Complete();
        Assert.IsNotNull(await store.GetAsync(f.User, Guid.Empty, "global-client", "resource"));
    }
    [TestMethod]
    public async Task NativeInvitationsAndCredentialsStopResolvingAsSoonAsTenantFencingStarts()
    {
        await using var f = await IdentityPurgeFixture.Create(); using var scope = f.Provider.CreateScope();
        var invites = scope.ServiceProvider.GetRequiredService<AzureTableTenantInviteStore>();
        Assert.HasCount(1, await invites.ListForTenantAsync(f.Tenant)); Assert.IsNotNull(await invites.GetAsync(f.Tenant, f.Invite));
        await f.Confirm(); await f.Run(); // Metadata fence precedes all app cleanup and the later credential-family pages.
        Assert.IsNull(await invites.GetAsync(f.Tenant, f.Invite));
        Assert.IsNull(await new AzureTableApiCredentialStore(f.Storage, Options.Create(f.NativeOptions)).GetAsync(f.Tenant, f.Credential));
        Assert.IsNull(await new AzureTableAgentUserStore(f.Storage, Options.Create(f.NativeOptions)).GetAsync(f.Tenant, f.Agent));
        Assert.IsNull(await new AzureTableOAuthConsentStore(f.Storage, Options.Create(f.NativeOptions)).GetAsync(f.User, f.Tenant, "fixture", "resource"));
        Assert.AreEqual(0, f.App.Effects.Count); await f.Complete();
    }
    [TestMethod]
    [DataRow("unprotected-provider")] [DataRow("missing-app-fence")] [DataRow("aliased-tables")]
    public async Task UnsupportedNativeProtectionOrMissingAppIntegrationPreventsStartup(string mode)
    {
        await using var f = await IdentityPurgeFixture.Create(includeAppFence: mode != "missing-app-fence");
        if (mode == "unprotected-provider") f.NativeOptions.RequireTenantPurgeFencing = false;
        if (mode == "aliased-tables") f.NativeOptions.TenantRolesTableName = f.NativeOptions.TenantUsersTableName;
        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            foreach (var hosted in f.Provider.GetServices<IHostedService>()) if (hosted is not BackgroundService) await hosted.StartAsync(default);
        });
        Assert.AreEqual(0, f.App.Effects.Count);
    }
    [TestMethod]
    [DataRow("AccessCatalogOverrides")] [DataRow("Tenants")]
    public async Task LostDeleteRepliesPauseAndReconcileWithoutRepeatingAppEffects(string family)
    {
        await using var f = await IdentityPurgeFixture.Create(); await f.Confirm(); f.Storage.LoseDeleteTable = f.Table(family).Name;
        ManagementTenantPurgeResult result = null!;
        for (var i = 0; i < 100; i++) { await f.Run(); result = await f.Status(); if (result.Job!.State == "unknown") break; }
        Assert.AreEqual("unknown", result.Job!.State); Assert.IsFalse(f.Audits.Any(x => x.Succeeded));
        await f.Run(); Assert.AreEqual("unknown", (await f.Status()).Job!.State);
        await f.Run(reconcile: true); await f.Complete();
        Assert.AreEqual(1, f.App.Effects.Count); Assert.IsNull((await f.Status()).Job!.FailureCode);
        Assert.DoesNotContain("PRIVATE-", JsonSerializer.Serialize(await f.Status()) + JsonSerializer.Serialize(f.Audits));
    }
    [TestMethod]
    public async Task KnownJobIdsAndSupportClaimsCannotConstructNativeDeletionAuthority()
    {
        await using var f = await IdentityPurgeFixture.Create(); await f.Confirm();
        using var scope = f.Provider.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<IIdentityTenantPurgeService>();
        await Assert.ThrowsAsync<IdentityUnauthorizedException>(() => service.FenceAsync(f.NativeScope()));
        await Assert.ThrowsAsync<IdentityUnauthorizedException>(() => service.CleanupAsync(f.NativeScope()));
        Assert.IsNotNull(await scope.ServiceProvider.GetRequiredService<AzureTableIdentityTenantStore>().FindByIdAsync(f.Tenant));
        Assert.AreEqual(0, f.App.Effects.Count);
    }
    [TestMethod]
    public async Task BoundedCheckpointsResumeAcrossScopesAndRemoveHistoricalOrphanIndexes()
    {
        await using var f = await IdentityPurgeFixture.Create(extraMembers: 24); await f.Confirm();
        for (var i = 0; i < 4; i++) await f.Run();
        var partial = await f.Status(); Assert.IsTrue(partial.Job!.State is "fencing" or "purging");
        await f.Complete();
        var stale = await f.Table("TenantInvites").GetEntityIfExistsAsync<TableEntity>("TOK|stale-token-hash", "INV"); Assert.IsFalse(stale.HasValue);
        var source = f.Table("TenantUsers"); var filter = TableClient.CreateQueryFilter($"PartitionKey eq {"TEN|" + f.Tenant.ToString("D")} and RowKey ge {"USR|"}");
        await foreach (var row in source.QueryAsync<TableEntity>(filter)) Assert.Fail("A tenant membership survived verified cleanup.");
        Assert.AreEqual(1, f.App.Effects.Count);
    }
    [TestMethod]
    [DataRow("token-index")] [DataRow("membership-index")]
    public async Task AnAlreadyPreparedLateIndexWriteCannotRecreateDataAfterCleanup(string kind)
    {
        await using var f = await IdentityPurgeFixture.Create(); using var scope = f.Provider.CreateScope(); var p = scope.ServiceProvider;
        var members = p.GetRequiredService<AzureTableTenantMembershipStore>(); var invites = p.GetRequiredService<AzureTableTenantInviteStore>();
        f.Storage.Match = action => action.ActionType != TableTransactionActionType.Delete && !action.Entity.RowKey.StartsWith("!IBEAM|", StringComparison.Ordinal) &&
            (kind == "token-index" ? action.Entity.PartitionKey.StartsWith("TOK|", StringComparison.Ordinal) :
                action.Entity.PartitionKey == "USR|" + f.User.ToString("D") && action.Entity.RowKey == "TEN|" + f.Tenant.ToString("D"));
        Task late;
        if (kind == "membership-index") late = members.RepairProjectionAsync(f.Tenant, f.User);
        else {
            var current = (await invites.GetAsync(f.Tenant, f.Invite))!;
            late = invites.UpdateAsync(current with { TokenHash = "late-new-token-hash" });
        }
        try {
            await f.Storage.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await f.Confirm(); await f.Complete(); f.Storage.Release();
            await Assert.ThrowsAsync<IdentityException>(() => late);
            var partition = kind == "token-index" ? "TOK|late-new-token-hash" : "USR|" + f.User.ToString("D");
            var rowKey = kind == "token-index" ? "INV" : "TEN|" + f.Tenant.ToString("D");
            Assert.IsFalse((await f.Table(kind == "token-index" ? "TenantInvites" : "UserTenants").GetEntityIfExistsAsync<TableEntity>(partition, rowKey)).HasValue);
        } finally { f.Storage.Release(); }
    }
    [TestMethod]
    [DataRow("tenant-create")] [DataRow("tenant-activate")] [DataRow("role-create")] [DataRow("membership")] [DataRow("invite-create")]
    [DataRow("api-key")] [DataRow("agent")] [DataRow("consent")]
    public async Task OrdinaryWritersCannotRecreateACompletedPurge(string operation)
    {
        await using var f = await IdentityPurgeFixture.Create(); await f.Confirm(); await f.Complete();
        using var scope = f.Provider.CreateScope(); var p = scope.ServiceProvider; var o = f.NativeOptions;
        Task Write() => operation switch {
            "tenant-create" => p.GetRequiredService<AzureTableIdentityTenantStore>().CreateAsync(new(f.Tenant, "PRIVATE-RECREATE")),
            "tenant-activate" => p.GetRequiredService<AzureTableIdentityTenantStore>().SetStatusAsync(f.Tenant, "Active"),
            "role-create" => p.GetRequiredService<AzureTableTenantRoleStore>().CreateRoleAsync(f.Tenant, "New role"),
            "membership" => p.GetRequiredService<AzureTableTenantRoleStore>().EnsureTenantMembershipAndGrantRolesAsync(new(f.Tenant, Guid.NewGuid(), "PRIVATE-RECREATE", RoleNames: ["Administrator"], SetAsDefault: false)),
            "invite-create" => p.GetRequiredService<AzureTableTenantInviteStore>().CreateAsync(f.Invitation() with { InviteId = Guid.NewGuid() }),
            "api-key" => new AzureTableApiCredentialStore(f.Storage, Options.Create(o)).CreateAsync(new(Guid.NewGuid(), f.Tenant, "fixture", null, "fixture", "PRIVATE-HASH", [], [], DateTimeOffset.UtcNow, null, null, null, null, null, null, null, null, false)),
            "agent" => new AzureTableAgentUserStore(f.Storage, Options.Create(o)).CreateAsync(new(Guid.NewGuid(), f.Tenant, "fixture", null, "service", "fixture", "active", DateTimeOffset.UtcNow, null, null, null)),
            _ => new AzureTableOAuthConsentStore(f.Storage, Options.Create(o)).UpsertAsync(new(Guid.NewGuid(), f.User, f.Tenant, "fixture", "resource", ["read"], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow))
        };
        await Assert.ThrowsAsync<IdentityException>(Write);
        Assert.IsNull(await p.GetRequiredService<AzureTableIdentityTenantStore>().FindByIdAsync(f.Tenant));
        Assert.IsNull(await new AzureTableApiCredentialStore(f.Storage, Options.Create(o)).GetAsync(f.Tenant, f.Credential));
        Assert.AreEqual("succeeded", (await f.Status()).Job!.State);
    }
}

[TestClass]
public sealed class IdentityPurgeRegistrationTests
{
    [TestMethod]
    public void DisabledBridgeInstallsNoNativeAuthorityOrConsumerHandler()
    {
        var services = new ServiceCollection(); services.AddIBeamManagementTenantPurgeIdentity<IdentityPurgeFixture.AppHandler>(_ => { });
        using var p = services.BuildServiceProvider(); Assert.IsNull(p.GetService<IIdentityTenantPurgeService>()); Assert.IsNull(p.GetService<IIdentityTenantPurgeAuthority>());
        Assert.IsNull(p.GetService<IManagementTenantPurgeHandler>());
    }
}

