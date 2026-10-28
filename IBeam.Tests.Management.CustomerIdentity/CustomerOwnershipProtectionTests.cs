using System.Net;
using System.Text.Json;
using Azure.Data.Tables;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Repositories.AzureTable.Options;
using IBeam.Identity.Repositories.AzureTable.Extensions;
using IBeam.Identity.Repositories.AzureTable.Stores;
using IBeam.Identity.Services.Tenants;
using IBeam.Management.CustomerIdentity;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class CustomerOwnershipProtectionTests
{
    private static IdentityTenantOwnershipProtection Protection(MembershipStorageFixture f,
        IIdentityTenantMembershipConditionalStore? store = null, params string[] names)
        => new(store ?? f.Members, f.Roles, Options.Create(new IdentityTenantOwnershipOptions {
            Enabled = true, OwnerRoleNames = names.Length == 0 ? [f.Owner.Name] : names }), new ServiceOperationExecutor());
    private static IdentityTenantMembershipAdministrationService Administration(MembershipStorageFixture f,
        IIdentityTenantMembershipConditionalStore? store = null)
        => new(store ?? f.Members, f.Roles, f.Tenants, new ServiceOperationExecutor(), Protection(f, store));
    private static async Task<TenantRole> AddAdministrator(MembershipStorageFixture f)
    {
        var role = await f.Roles.CreateRoleAsync(f.Tenant, "Administrator", true);
        await f.Roles.EnsureTenantMembershipAndGrantRolesAsync(new(f.Tenant, f.Member, RoleIds: [role.RoleId]));
        return role;
    }
    [TestMethod, TestCategory("StorageIntegration")]
    [DataRow("disable")] [DataRow("replace")] [DataRow("revoke")]
    public async Task AnAdministratorCannotSubstituteForTheFinalOwner(string action)
    {
        await using var f = await MembershipStorageFixture.Create(ownershipProtection: true, ownerRoleName: "EnterpriseCustodian");
        var administrator = await AddAdministrator(f); var service = Administration(f);
        var admins = new[] { f.Owner.RoleId, administrator.RoleId };
        var a = (await f.Members.FindStateAsync(f.Tenant, f.AdminA))!;
        var other = (await f.Members.FindStateAsync(f.OtherTenant, f.AdminA))!;
        await service.SetEnabledAsync(f.Tenant, f.AdminA, false, a.Version, Guid.NewGuid(), admins);
        var b = (await f.Members.FindStateAsync(f.Tenant, f.AdminB))!;
        var snapshot = await f.Members.ReadSnapshotAsync(f.Tenant, 5000);
        Task Change() => action switch {
            "disable" => service.SetEnabledAsync(f.Tenant, f.AdminB, false, b.Version, Guid.NewGuid(), admins),
            "replace" => service.SetRolesAsync(f.Tenant, f.AdminB, [], b.Version, Guid.NewGuid(), admins),
            _ => new TenantRoleService(service, f.Roles, new NoOpTenantExtensionCoordinator()).RevokeRolesAsync(f.Tenant, f.AdminB, [f.Owner.RoleId])
        };
        await Assert.ThrowsAsync<IdentityValidationException>(Change);
        var after = (await f.Members.FindStateAsync(f.Tenant, f.AdminB))!;
        Assert.AreEqual(b.Version, after.Version); Assert.IsTrue(after.IsEnabled); Assert.Contains(f.Owner.RoleId, after.RoleIds);
        Assert.AreEqual(snapshot.Version, (await f.Members.ReadSnapshotAsync(f.Tenant, 5000)).Version);
        Assert.AreEqual(other.Version, (await f.Members.FindStateAsync(f.OtherTenant, f.AdminA))!.Version);
        Assert.Contains(administrator.RoleId, (await f.Members.FindStateAsync(f.Tenant, f.Member))!.RoleIds);
        Assert.HasCount(1, snapshot.ProtectedOwnerRoleIds); Assert.AreEqual(1, snapshot.OwnershipProtocolVersion);
        var row = (await f.Primary.GetEntityAsync<TableEntity>("TEN|" + f.Tenant, "USR|" + f.AdminB)).Value;
        Assert.AreEqual("KEEP-CONSUMER-COLUMN", row.GetString("ConsumerColumn"));
        Assert.AreEqual("PRIVATE-CUSTOMER-EMAIL", row.GetString("Email"));
    }
    [TestMethod, TestCategory("StorageIntegration")]
    [DataRow(false)] [DataRow(true)]
    public async Task SharedRevisionAllowsOnlyOneOfTwoConcurrentOwnerRemovals(bool roles)
    {
        await using var f = await MembershipStorageFixture.Create(ownershipProtection: true); var admin = await AddAdministrator(f);
        await Protection(f).EnsureAsync(f.Tenant); await f.Members.ConfigureProtectionAsync(f.Tenant, [admin.RoleId]);
        var barrier = new SnapshotBarrier(f.Members); var service = Administration(f, barrier);
        var first = (await f.Members.FindStateAsync(f.Tenant, f.AdminA))!; var second = (await f.Members.FindStateAsync(f.Tenant, f.AdminB))!;
        async Task<bool> Change(IdentityTenantMembershipState member) {
            try {
                if (roles) await service.SetRolesAsync(f.Tenant, member.UserId, [], member.Version, Guid.NewGuid(), [admin.RoleId]);
                else await service.SetEnabledAsync(f.Tenant, member.UserId, false, member.Version, Guid.NewGuid(), [admin.RoleId]);
                return true;
            } catch (IdentityConcurrencyException) { return false; }
        }
        var result = await Task.WhenAll(Change(first), Change(second)); Assert.AreEqual(1, result.Count(x => x));
        var after = await f.Members.ReadSnapshotAsync(f.Tenant, 5000);
        Assert.AreEqual(1, after.Members.Count(x => x.IsEnabled && x.RoleIds.Contains(f.Owner.RoleId)));
        Assert.IsTrue(after.Members.Single(x => x.UserId == f.Member).IsEnabled);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task NativeRoleRevocationProtectsOwnersWithoutAnAdministratorGroup()
    {
        await using var f = await MembershipStorageFixture.Create(ownershipProtection: true);
        var native = new TenantRoleService(Administration(f), f.Roles, new NoOpTenantExtensionCoordinator());
        await native.RevokeRolesAsync(f.Tenant, f.AdminA, [f.Owner.RoleId]);
        Assert.HasCount(0, (await f.Members.FindStateAsync(f.Tenant, f.AdminA))!.RoleIds);
        await Assert.ThrowsAsync<IdentityValidationException>(() => native.RevokeRolesAsync(f.Tenant, f.AdminB, [f.Owner.RoleId]));
        var snapshot = await f.Members.ReadSnapshotAsync(f.Tenant, 5000);
        Assert.HasCount(0, snapshot.ProtectedAdministratorRoleIds); Assert.HasCount(1, snapshot.ProtectedOwnerRoleIds);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    [DataRow("membership")] [DataRow("roles")] [DataRow("definition")] [DataRow("legacy-service")]
    public async Task UncoordinatedDestructiveWritersFailBeforeOwnerActivation(string action)
    {
        await using var f = await MembershipStorageFixture.Create(ownershipProtection: true);
        var before = (await f.Members.FindStateAsync(f.Tenant, f.AdminA))!;
        Task Change() => action switch {
            "membership" => f.Members.DisableTenantMembershipAsync(f.Tenant, f.AdminA),
            "roles" => f.Roles.RevokeRolesAsync(f.Tenant, f.AdminA, [f.Owner.RoleId]),
            "definition" => f.Roles.DeleteRoleAsync(f.Tenant, f.MemberRole.RoleId),
            _ => new TenantRoleService(f.Roles).RevokeRolesAsync(f.Tenant, f.AdminA, [f.Owner.RoleId])
        };
        await Assert.ThrowsAsync<IdentityValidationException>(Change);
        Assert.AreEqual(before.Version, (await f.Members.FindStateAsync(f.Tenant, f.AdminA))!.Version);
        Assert.IsTrue((await f.Roles.GetRoleAsync(f.Tenant, f.MemberRole.RoleId))!.IsActive);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task BootstrapAndInvitationGrantCannotEraseLegacyNamedOwnership()
    {
        await using var f = await MembershipStorageFixture.Create(ownershipProtection: true);
        var row = (await f.Primary.GetEntityAsync<TableEntity>("TEN|" + f.Tenant, "USR|" + f.AdminA)).Value;
        row["RoleIdsCsv"] = ""; row["RolesCsv"] = "owner"; await f.Primary.UpdateEntityAsync(row, row.ETag, TableUpdateMode.Replace);
        await Assert.ThrowsAsync<IdentityValidationException>(() => f.Roles.EnsureTenantMembershipAndGrantRolesAsync(new(f.Tenant, f.AdminA, RoleIds: [f.MemberRole.RoleId])));
        var after = (await f.Members.FindStateAsync(f.Tenant, f.AdminA))!;
        Assert.HasCount(0, after.RoleIds); Assert.Contains("owner", after.RoleNames); Assert.IsTrue(after.IsEnabled);
        var service = Administration(f); var b = (await f.Members.FindStateAsync(f.Tenant, f.AdminB))!;
        await service.SetEnabledAsync(f.Tenant, f.AdminB, false, b.Version, Guid.NewGuid(), [f.Owner.RoleId]);
        after = (await f.Members.FindStateAsync(f.Tenant, f.AdminA))!;
        await Assert.ThrowsAsync<IdentityValidationException>(() => service.SetEnabledAsync(f.Tenant, f.AdminA, false, after.Version, Guid.NewGuid(), [f.Owner.RoleId]));
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task RetryRepairsProjectionWithoutRepeatingAnOwnerChange()
    {
        await using var f = await MembershipStorageFixture.Create(ownershipProtection: true); var admin = await AddAdministrator(f);
        var wrapper = new SnapshotBarrier(f.Members) { WaitForPair = false, FailProjectionOnce = true }; var service = Administration(f, wrapper);
        var before = (await f.Members.FindStateAsync(f.Tenant, f.AdminA))!; var operation = Guid.NewGuid();
        await Assert.ThrowsAsync<IdentityProviderException>(() => service.SetEnabledAsync(f.Tenant, f.AdminA, false, before.Version, operation, [admin.RoleId]));
        var written = (await f.Members.FindStateAsync(f.Tenant, f.AdminA))!;
        var replay = await service.SetEnabledAsync(f.Tenant, f.AdminA, false, before.Version, operation, [admin.RoleId]);
        Assert.AreEqual(written.Version, replay.Version); Assert.IsFalse(replay.IsEnabled);
        Assert.IsTrue((await f.Members.FindStateAsync(f.Tenant, f.AdminB))!.IsEnabled);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task PersistedOwnerPolicySurvivesAHostWithoutTheOptInFlag()
    {
        await using var f = await MembershipStorageFixture.Create(ownershipProtection: true); await Protection(f).EnsureAsync(f.Tenant);
        var options = Options.Create(new AzureTableIdentityOptions { TenantUsersTableName = f.Primary.Name, UserTenantsTableName = f.Secondary.Name,
            TenantRolesTableName = f.RoleTable.Name, TenantsTableName = f.TenantTable.Name, CreateTablesIfNotExists = false });
        var tables = new TableServiceClient(Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_STORAGE"));
        var members = new AzureTableTenantMembershipStore(tables, options); var roles = new AzureTableTenantRoleStore(tables, options);
        var service = new IdentityTenantMembershipAdministrationService(members, roles, f.Tenants, new ServiceOperationExecutor());
        var a = (await members.FindStateAsync(f.Tenant, f.AdminA))!;
        await service.SetEnabledAsync(f.Tenant, f.AdminA, false, a.Version, Guid.NewGuid(), [f.Owner.RoleId]);
        var b = (await members.FindStateAsync(f.Tenant, f.AdminB))!;
        await Assert.ThrowsAsync<IdentityValidationException>(() => service.SetEnabledAsync(f.Tenant, f.AdminB, false, b.Version, Guid.NewGuid(), [f.Owner.RoleId]));
        await Assert.ThrowsAsync<IdentityValidationException>(() => members.DisableTenantMembershipAsync(f.Tenant, f.AdminB));
        await Assert.ThrowsAsync<IdentityValidationException>(() => roles.RevokeRolesAsync(f.Tenant, f.AdminB, [f.Owner.RoleId]));
        await Assert.ThrowsAsync<IdentityValidationException>(() => f.Members.ConfigureOwnershipProtectionAsync(f.Tenant, []));
        var alternate = await f.Roles.CreateRoleAsync(f.Tenant, "AlternateCustodian", true);
        await Assert.ThrowsAsync<IdentityConcurrencyException>(() => Protection(f, null, f.Owner.Name, alternate.Name).EnsureAsync(f.Tenant));
        Assert.Contains(f.Owner.RoleId, (await members.FindStateAsync(f.Tenant, f.AdminB))!.RoleIds);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task PersistedOwnersRejectAChangedRoleIdentityWithoutRelyingOnOptInConfiguration()
    {
        await using var f = await MembershipStorageFixture.Create(ownershipProtection: true); var administrator = await AddAdministrator(f);
        await Protection(f).EnsureAsync(f.Tenant);
        var row = (await f.RoleTable.GetEntityAsync<TableEntity>("TEN|" + f.Tenant, "ROL|" + f.Owner.RoleId)).Value;
        row["RoleId"] = Guid.NewGuid().ToString("D"); await f.RoleTable.UpdateEntityAsync(row, row.ETag, TableUpdateMode.Replace);
        var before = (await f.Members.FindStateAsync(f.Tenant, f.AdminA))!;
        var service = new IdentityTenantMembershipAdministrationService(f.Members, f.Roles, f.Tenants, new ServiceOperationExecutor());
        await Assert.ThrowsAsync<IdentityValidationException>(() => service.SetEnabledAsync(f.Tenant, f.AdminA, false, before.Version, Guid.NewGuid(), [administrator.RoleId]));
        Assert.AreEqual(before.Version, (await f.Members.FindStateAsync(f.Tenant, f.AdminA))!.Version);
        Assert.Contains(f.Owner.RoleId, (await f.Members.FindStateAsync(f.Tenant, f.AdminB))!.RoleIds);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    [DataRow("missing")] [DataRow("mutable")] [DataRow("inactive")] [DataRow("ambiguous")] [DataRow("foreign")]
    public async Task InvalidOwnerDefinitionsFailBeforeInstallingProtection(string kind)
    {
        await using var f = await MembershipStorageFixture.Create(ownershipProtection: true); var name = f.Owner.Name;
        if (kind == "missing") name = "UninstalledCustodian";
        if (kind == "mutable") name = f.MemberRole.Name;
        if (kind is "inactive" or "foreign") {
            var row = (await f.RoleTable.GetEntityAsync<TableEntity>("TEN|" + f.Tenant, "ROL|" + f.Owner.RoleId)).Value;
            if (kind == "inactive") row["Status"] = "Disabled"; else row["TenantId"] = f.OtherTenant.ToString("D");
            await f.RoleTable.UpdateEntityAsync(row, row.ETag, TableUpdateMode.Replace);
        }
        if (kind == "ambiguous") {
            var row = (await f.RoleTable.GetEntityAsync<TableEntity>("TEN|" + f.Tenant, "ROL|" + f.Owner.RoleId)).Value;
            var id = Guid.NewGuid(); row.RowKey = "ROL|" + id; row["RoleId"] = id.ToString("D"); await f.RoleTable.AddEntityAsync(row);
        }
        var before = (await f.Members.FindStateAsync(f.Tenant, f.AdminA))!;
        await Assert.ThrowsAsync<IdentityValidationException>(() => Protection(f, null, name).EnsureAsync(f.Tenant));
        Assert.AreEqual(before.Version, (await f.Members.FindStateAsync(f.Tenant, f.AdminA))!.Version);
        Assert.HasCount(0, (await f.Members.ReadSnapshotAsync(f.Tenant, 5000)).ProtectedOwnerRoleIds);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task UnsupportedWritersAndGlobalAccountDestructionAreRejected()
    {
        await using var legacy = await MembershipStorageFixture.Create();
        Assert.Throws<InvalidOperationException>(() => Protection(legacy));
        await using var f = await MembershipStorageFixture.Create(ownershipProtection: true);
        Assert.Throws<InvalidOperationException>(() => new IdentityTenantOwnershipProtection(f.Members, Mock.Of<ITenantRoleStore>(),
            Options.Create(new IdentityTenantOwnershipOptions { Enabled = true, OwnerRoleNames = ["Owner"] }), new ServiceOperationExecutor()));
        var before = (await f.Members.FindStateAsync(f.Tenant, f.AdminA))!;
        await Assert.ThrowsAsync<IdentityValidationException>(() => Protection(f).RequireGlobalAccountMutationAsync(f.AdminA));
        Assert.AreEqual(before.Version, (await f.Members.FindStateAsync(f.Tenant, f.AdminA))!.Version);
    }
    [TestMethod]
    public void OwnershipRequiresExplicitValidNamesAndDoesNotInferOwner()
    {
        var defaults = new IdentityTenantOwnershipOptions(); Assert.IsTrue(defaults.IsValid()); Assert.HasCount(0, defaults.OwnerRoleNames);
        Assert.IsFalse(new IdentityTenantOwnershipOptions { Enabled = true }.IsValid());
        Assert.IsFalse(new IdentityTenantOwnershipOptions { OwnerRoleNames = ["Owner"] }.IsValid());
        Assert.IsFalse(new IdentityTenantOwnershipOptions { Enabled = true, OwnerRoleNames = ["Owner", "owner"] }.IsValid());
        Assert.IsFalse(new IdentityTenantOwnershipOptions { Enabled = true, OwnerRoleNames = ["*"] }.IsValid());
        Assert.IsTrue(new IdentityTenantOwnershipOptions { Enabled = true, OwnerRoleNames = ["EnterpriseCustodian"] }.IsValid());
    }
    [TestMethod, TestCategory("StorageIntegration")]
    [DataRow(false)] [DataRow(true)]
    public async Task SignedManagementCommandsEnforceOwnerProtectionIndependentlyOfAdmin(bool roles)
    {
        await using var data = await MembershipStorageFixture.Create(ownershipProtection: true); await AddAdministrator(data);
        await using var target = await DelegationFixture.Start(memberships: data, ownershipRoleNames: [data.Owner.Name], membershipAdministratorRoleNames: ["Administrator"]);
        using var info = await target.Send(null, path: "/api/management-identity/info");
        using var document = JsonDocument.Parse(await info.Content.ReadAsStringAsync());
        Assert.AreEqual(1, document.RootElement.GetProperty("data").GetProperty("ownershipProtectionProtocolVersion").GetInt32());
        Assert.IsFalse(document.RootElement.GetProperty("data").GetProperty("capabilities").EnumerateArray().Any(x => x.GetString()!.EndsWith("owners.transfer", StringComparison.Ordinal)));
        var a = (await data.Members.FindStateAsync(data.Tenant, data.AdminA))!;
        using var first = await Write(target, data, data.AdminA, roles, a.Version, "owner-first-change"); Assert.AreEqual(HttpStatusCode.OK, first.StatusCode, await first.Content.ReadAsStringAsync());
        var b = (await data.Members.FindStateAsync(data.Tenant, data.AdminB))!;
        using var rejected = await Write(target, data, data.AdminB, roles, b.Version, "owner-final-change"); Assert.AreEqual(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.DoesNotContain("PRIVATE-CUSTOMER", await rejected.Content.ReadAsStringAsync());
        Assert.AreEqual(b.Version, (await data.Members.FindStateAsync(data.Tenant, data.AdminB))!.Version);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task DefaultTargetDoesNotAdvertiseOwnershipProtection()
    {
        await using var data = await MembershipStorageFixture.Create(); await using var target = await DelegationFixture.Start(memberships: data);
        using var info = await target.Send(null, path: "/api/management-identity/info");
        using var document = JsonDocument.Parse(await info.Content.ReadAsStringAsync());
        Assert.IsFalse(document.RootElement.GetProperty("data").TryGetProperty("ownershipProtectionProtocolVersion", out _));
    }
    [TestMethod, TestCategory("StorageIntegration")]
    [DataRow(false)] [DataRow(true)]
    public async Task InvalidOwnershipConfigurationCannotStartAnAdvertisedManagementTarget(bool invalidPolicy)
    {
        await using var data = await MembershipStorageFixture.Create(ownershipProtection: invalidPolicy);
        async Task Start() { await using var target = await DelegationFixture.Start(memberships: data,
            ownershipRoleNames: invalidPolicy ? [] : ["Owner"]); }
        if (invalidPolicy) await Assert.ThrowsAsync<OptionsValidationException>(Start);
        else await Assert.ThrowsAsync<InvalidOperationException>(Start);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task UnavailableOwnershipStorageFailsClosedAndSameIntentCanRecover()
    {
        await using var data = await MembershipStorageFixture.Create(ownershipProtection: true); await AddAdministrator(data);
        var wrapper = new SnapshotBarrier(data.Members) { WaitForPair = false, FailOwnershipOnce = true };
        var receipts = new OperationStore();
        await using var target = await DelegationFixture.Start(memberships: data, membershipWrites: wrapper, operations: receipts,
            ownershipRoleNames: [data.Owner.Name], membershipAdministratorRoleNames: ["Administrator"]);
        var before = (await data.Members.FindStateAsync(data.Tenant, data.AdminA))!;
        using var failed = await Write(target, data, data.AdminA, false, before.Version, "owner-provider-recovery");
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.DoesNotContain("PRIVATE-OWNERSHIP", await failed.Content.ReadAsStringAsync());
        Assert.AreEqual(before.Version, (await data.Members.FindStateAsync(data.Tenant, data.AdminA))!.Version);
        using var recovered = await Write(target, data, data.AdminA, false, before.Version, "owner-provider-recovery");
        Assert.AreEqual(HttpStatusCode.OK, recovered.StatusCode, await recovered.Content.ReadAsStringAsync());
        Assert.HasCount(1, receipts.Rows); Assert.IsFalse((await data.Members.FindStateAsync(data.Tenant, data.AdminA))!.IsEnabled);
        Assert.IsTrue((await data.Members.FindStateAsync(data.Tenant, data.AdminB))!.IsEnabled);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    [DataRow(false)] [DataRow(true)]
    public async Task MissingOwnershipRegistrationOrSnapshotMetadataCannotBypassTheGuard(bool corruptSnapshot)
    {
        await using var f = await MembershipStorageFixture.Create(ownershipProtection: true);
        var before = (await f.Members.FindStateAsync(f.Tenant, f.AdminA))!;
        IIdentityTenantMembershipAdministrationService service = corruptSnapshot
            ? Administration(f, new SnapshotBarrier(f.Members) { WaitForPair = false, DropOwnershipMetadata = true })
            : new IdentityTenantMembershipAdministrationService(f.Members, f.Roles, f.Tenants, new ServiceOperationExecutor());
        await Assert.ThrowsAsync<IdentityProviderException>(() => service.SetEnabledAsync(f.Tenant, f.AdminA, false, before.Version, Guid.NewGuid(), [f.Owner.RoleId]));
        Assert.AreEqual(before.Version, (await f.Members.FindStateAsync(f.Tenant, f.AdminA))!.Version);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    [DataRow(false)] [DataRow(true)]
    public async Task ConsumerRegistrationBindsTheProviderFenceAndIsIdempotent(bool enabled)
    {
        await using var f = await MembershipStorageFixture.Create();
        var values = new Dictionary<string, string?> {
            [IdentityTenantOwnershipOptions.SectionName + ":Enabled"] = enabled.ToString(),
            [AzureTableIdentityOptions.SectionName + ":StorageConnectionString"] = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_STORAGE"),
            [AzureTableIdentityOptions.SectionName + ":CreateTablesIfNotExists"] = "false",
            [AzureTableIdentityOptions.SectionName + ":TenantUsersTableName"] = f.Primary.Name,
            [AzureTableIdentityOptions.SectionName + ":UserTenantsTableName"] = f.Secondary.Name,
            [AzureTableIdentityOptions.SectionName + ":TenantRolesTableName"] = f.RoleTable.Name,
            [AzureTableIdentityOptions.SectionName + ":TenantsTableName"] = f.TenantTable.Name
        };
        if (enabled) values[IdentityTenantOwnershipOptions.SectionName + ":OwnerRoleNames:0"] = f.Owner.Name;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build(); var services = new ServiceCollection();
        services.AddLogging(); services.AddSingleton<IServiceOperationExecutor>(new ServiceOperationExecutor());
        services.AddIBeamIdentityAzureTable(configuration, registerSchemaHostedService: false);
        services.AddIBeamIdentityTenantOwnershipProtection(configuration); services.AddIBeamIdentityTenantOwnershipProtection(configuration);
        await using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        Assert.AreEqual(enabled, scope.ServiceProvider.GetRequiredService<IOptions<AzureTableIdentityOptions>>().Value.RequireOwnershipProtection);
        var protection = scope.ServiceProvider.GetService<IIdentityTenantOwnershipProtection>();
        if (!enabled) { Assert.IsNull(protection); return; }
        Assert.IsNotNull(protection); Assert.AreEqual(1, protection.ProtocolVersion);
        await protection.EnsureAsync(f.Tenant);
        Assert.Contains(f.Owner.RoleId, (await f.Members.ReadSnapshotAsync(f.Tenant, 5000)).ProtectedOwnerRoleIds);
    }
    private static async Task<HttpResponseMessage> Write(DelegationFixture target, MembershipStorageFixture data, Guid user, bool roles, string version, string key)
    {
        var operation = roles ? ManagementCustomerIdentityOperations.RolesAssign : ManagementCustomerIdentityOperations.MembershipsManage;
        var method = roles ? "PUT" : "PATCH"; var path = $"/api/management-identity/tenants/{data.Tenant}/users/{user}/" + (roles ? "roles" : "membership");
        object request = roles ? new { policyKey = "administrator", roleIds = Array.Empty<Guid>() } : new { isEnabled = false };
        var body = JsonSerializer.SerializeToUtf8Bytes(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return await target.Send(await target.Issue(operation, method, path, body, key, version), method, path, body, key, version);
    }
}
