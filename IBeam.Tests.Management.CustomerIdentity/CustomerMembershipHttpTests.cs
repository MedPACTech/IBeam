using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Data.Tables;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Services.Tenants;
using IBeam.Management.CustomerIdentity;
using IBeam.Services.Abstractions;
namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class CustomerMembershipHttpTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static async Task<HttpResponseMessage> Write(DelegationFixture f, MembershipStorageFixture data, Guid user, object request, string key, string version, bool roles = false)
    {
        var path = $"/api/management-identity/tenants/{data.Tenant}/users/{user}/" + (roles ? "roles" : "membership");
        var authority = roles ? ManagementCustomerIdentityOperations.RolesAssign : ManagementCustomerIdentityOperations.MembershipsManage;
        var method = roles ? "PUT" : "PATCH"; var bytes = JsonSerializer.SerializeToUtf8Bytes(request, Json);
        return await f.Send(await f.Issue(authority, method, path, bytes, key, version), method, path, bytes, key, version);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task SignedStatusIsVersionedIdempotentAndIndependentOfRoleAssignment()
    {
        await using var data = await MembershipStorageFixture.Create(); await using var f = await DelegationFixture.Start(memberships: data);
        var current = (await data.Members.FindStateAsync(data.Tenant, data.Member))!;
        using var first = await Write(f, data, data.Member, new { isEnabled = false }, "membership-status-first", current.Version);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode, await first.Content.ReadAsStringAsync()); Assert.DoesNotContain("PRIVATE-CUSTOMER", await first.Content.ReadAsStringAsync());
        var disabled = (await data.Members.FindStateAsync(data.Tenant, data.Member))!;
        using var replay = await Write(f, data, data.Member, new { isEnabled = false }, "membership-status-first", current.Version);
        Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode); Assert.AreEqual(disabled.Version, (await data.Members.FindStateAsync(data.Tenant, data.Member))!.Version);
        using var changed = await Write(f, data, data.Member, new { isEnabled = true }, "membership-status-first", current.Version); Assert.AreEqual(HttpStatusCode.Conflict, changed.StatusCode);
        using var stale = await Write(f, data, data.Member, new { isEnabled = true }, "membership-status-stale", current.Version); Assert.AreEqual(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        f.Credentials.Row = f.Credentials.Row with { Capabilities = f.Credentials.Row.Capabilities.Where(x => x != ManagementCustomerIdentityOperations.MembershipsManage).ToArray() };
        using var denied = await Write(f, data, data.Member, new { isEnabled = true }, "membership-status-denied", disabled.Version); Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var roles = await Write(f, data, data.Member, new { policyKey = "default", roleIds = Array.Empty<Guid>() }, "membership-role-clear", disabled.Version, true);
        Assert.AreEqual(HttpStatusCode.OK, roles.StatusCode, await roles.Content.ReadAsStringAsync());
        var after = (await data.Members.FindStateAsync(data.Tenant, data.Member))!; Assert.IsFalse(after.IsEnabled); Assert.HasCount(0, after.RoleIds);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task CrossTenantRolesAndUnknownPayloadFieldsCannotChangeMembership()
    {
        await using var data = await MembershipStorageFixture.Create(); await using var f = await DelegationFixture.Start(memberships: data);
        var current = (await data.Members.FindStateAsync(data.Tenant, data.Member))!;
        using var foreign = await Write(f, data, data.Member, new { policyKey = "default", roleIds = new[] { Guid.NewGuid() } }, "membership-foreign-role", current.Version, true);
        Assert.AreEqual(HttpStatusCode.Forbidden, foreign.StatusCode);
        using var unknown = await Write(f, data, data.Member, new { isEnabled = false, reason = "PRIVATE-PROFILE-CANARY" }, "membership-private-reason", current.Version);
        Assert.AreEqual(HttpStatusCode.BadRequest, unknown.StatusCode); Assert.DoesNotContain("PRIVATE-PROFILE", await unknown.Content.ReadAsStringAsync());
        using var absent = await Write(f, data, data.Member, new { }, "membership-missing-status", current.Version); Assert.AreEqual(HttpStatusCode.BadRequest, absent.StatusCode);
        Assert.AreEqual(current.Version, (await data.Members.FindStateAsync(data.Tenant, data.Member))!.Version);
        var path = $"/api/management-identity/tenants/{data.OtherTenant}/users/{data.Member}/membership"; var bytes = Encoding.UTF8.GetBytes("{\"isEnabled\":false}");
        using var cross = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.MembershipsManage, "PATCH", path, bytes, ifMatch: current.Version), "PATCH", path, bytes, ifMatch: current.Version);
        Assert.AreEqual(HttpStatusCode.NotFound, cross.StatusCode);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    [DataRow(false)] [DataRow(true)]
    public async Task LostProjectionReplyReconcilesThroughOneDurableOperationWithoutAnotherPrimaryWrite(bool roles)
    {
        await using var data = await MembershipStorageFixture.Create(); var wrapper = new SnapshotBarrier(data.Members) { WaitForPair = false, FailProjectionOnce = true };
        await using var f = await DelegationFixture.Start(memberships: data, membershipWrites: wrapper);
        var original = (await data.Members.FindStateAsync(data.Tenant, data.Member))!;
        object change = roles ? new { policyKey = "default", roleIds = Array.Empty<Guid>() } : new { isEnabled = false };
        using var failed = await Write(f, data, data.Member, change, "membership-recovery-intent", original.Version, roles); Assert.AreEqual(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        var applied = (await data.Members.FindStateAsync(data.Tenant, data.Member))!;
        if (roles) Assert.HasCount(0, applied.RoleIds); else Assert.IsFalse(applied.IsEnabled);
        using var recovered = await Write(f, data, data.Member, change, "membership-recovery-intent", original.Version, roles);
        Assert.AreEqual(HttpStatusCode.OK, recovered.StatusCode, await recovered.Content.ReadAsStringAsync()); Assert.AreEqual(applied.Version, (await data.Members.FindStateAsync(data.Tenant, data.Member))!.Version);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    [DataRow(false, false)] [DataRow(true, false)] [DataRow(false, true)]
    public async Task NarrowPolicyCannotStripAnExistingOutOfPolicyRole(bool customRole, bool clear)
    {
        await using var data = await MembershipStorageFixture.Create();
        var protectedRole = customRole ? await data.Roles.CreateRoleAsync(data.Tenant, "EnterpriseCustodian", true) : data.Owner;
        await SeedRoles(data, data.AdminA, [protectedRole, data.MemberRole]);
        var current = (await data.Members.FindStateAsync(data.Tenant, data.AdminA))!;
        var secondary = (await data.Secondary.GetEntityAsync<TableEntity>("USR|" + data.AdminA, "TEN|" + data.Tenant)).Value;
        var other = (await data.Members.FindStateAsync(data.OtherTenant, data.AdminA))!;
        var access = new IdentityTenantAccessValidator(data.Members, data.Tenants, new ServiceOperationExecutor());
        var beforeAccess = await access.RequireAsync(data.AdminA, data.Tenant, null);
        var receipts = new OperationStore();
        await using var f = await DelegationFixture.Start(memberships: data, operations: receipts);
        using var rejected = await Write(f, data, data.AdminA, new { policyKey = "default", roleIds = clear ? Array.Empty<Guid>() : new[] { data.MemberRole.RoleId } },
            "cannot-strip-protected-role", current.Version, true);
        Assert.AreEqual(HttpStatusCode.Forbidden, rejected.StatusCode);
        Assert.DoesNotContain("EnterpriseCustodian", await rejected.Content.ReadAsStringAsync());
        Assert.DoesNotContain("PRIVATE-CUSTOMER", await rejected.Content.ReadAsStringAsync());
        Assert.AreEqual(current.Version, (await data.Members.FindStateAsync(data.Tenant, data.AdminA))!.Version);
        Assert.AreEqual(secondary.ETag, (await data.Secondary.GetEntityAsync<TableEntity>(secondary.PartitionKey, secondary.RowKey)).Value.ETag);
        Assert.AreEqual(other.Version, (await data.Members.FindStateAsync(data.OtherTenant, data.AdminA))!.Version);
        Assert.AreEqual(beforeAccess.Version, (await access.RequireAsync(data.AdminA, data.Tenant, beforeAccess.Version)).Version);
        Assert.HasCount(0, receipts.Rows);
        Assert.Contains(data.Owner.RoleId, (await data.Members.FindStateAsync(data.Tenant, data.AdminB))!.RoleIds);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task ApprovedRoleRemovalNeedsOnlyTheRoleAssignmentGrantAndReplaysWithoutAnotherWrite()
    {
        await using var data = await MembershipStorageFixture.Create(); await using var f = await DelegationFixture.Start(memberships: data);
        f.Credentials.Row = f.Credentials.Row with { Capabilities = f.Credentials.Row.Capabilities.Where(x => x is not
            (ManagementCustomerIdentityOperations.RolesRead or ManagementCustomerIdentityOperations.MembershipsManage)).ToArray() };
        var current = (await data.Members.FindStateAsync(data.Tenant, data.Member))!;
        var request = new { policyKey = "default", roleIds = Array.Empty<Guid>() };
        using var first = await Write(f, data, data.Member, request, "approved-role-removal", current.Version, true);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode, await first.Content.ReadAsStringAsync());
        var changed = (await data.Members.FindStateAsync(data.Tenant, data.Member))!;
        Assert.HasCount(0, changed.RoleIds); Assert.IsTrue(changed.IsEnabled);
        using var replay = await Write(f, data, data.Member, request, "approved-role-removal", current.Version, true);
        Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode);
        Assert.AreEqual(changed.Version, (await data.Members.FindStateAsync(data.Tenant, data.Member))!.Version);
        using var stale = await Write(f, data, data.Member, new { policyKey = "default", roleIds = new[] { data.MemberRole.RoleId } },
            "stale-role-replacement", current.Version, true);
        Assert.AreEqual(HttpStatusCode.PreconditionFailed, stale.StatusCode);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    [DataRow(true)] [DataRow(false)]
    public async Task UnmappedLegacyRoleNamesCannotBeSilentlyNormalizedByReplacement(bool namesOnly)
    {
        await using var data = await MembershipStorageFixture.Create();
        var row = (await data.Primary.GetEntityAsync<TableEntity>("TEN|" + data.Tenant, "USR|" + data.Member)).Value;
        row["RoleIdsCsv"] = namesOnly ? "" : data.MemberRole.RoleId.ToString("D"); row["RolesCsv"] = "Owner";
        await data.Primary.UpdateEntityAsync(row, row.ETag, TableUpdateMode.Replace);
        var current = (await data.Members.FindStateAsync(data.Tenant, data.Member))!; var receipts = new OperationStore();
        await using var f = await DelegationFixture.Start(memberships: data, operations: receipts);
        using var rejected = await Write(f, data, data.Member, new { policyKey = "default", roleIds = Array.Empty<Guid>() },
            "legacy-role-replacement", current.Version, true);
        Assert.AreEqual(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.AreEqual(current.Version, (await data.Members.FindStateAsync(data.Tenant, data.Member))!.Version);
        Assert.HasCount(0, receipts.Rows);
    }
    private static async Task SeedRoles(MembershipStorageFixture data, Guid user, IReadOnlyList<TenantRole> roles)
    {
        var row = (await data.Primary.GetEntityAsync<TableEntity>("TEN|" + data.Tenant, "USR|" + user)).Value;
        row["RoleIdsCsv"] = string.Join(',', roles.Select(x => x.RoleId)); row["RolesCsv"] = string.Join(',', roles.Select(x => x.Name));
        await data.Primary.UpdateEntityAsync(row, row.ETag, TableUpdateMode.Replace);
        await data.Members.RepairProjectionAsync(data.Tenant, user);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task ConcurrentNativeGrantAfterPolicyCheckCannotBeOverwrittenByRoleReplacement()
    {
        await using var data = await MembershipStorageFixture.Create(); var reads = 0;
        var wrapper = new SnapshotBarrier(data.Members) { WaitForPair = false, BeforeFindState = async (tenant, user, ct) => {
            if (tenant == data.Tenant && user == data.Member && Interlocked.Increment(ref reads) == 2)
                await data.Roles.EnsureTenantMembershipAndGrantRolesAsync(new(tenant, user, RoleIds: [data.Owner.RoleId]), ct);
        } };
        await using var f = await DelegationFixture.Start(memberships: data, membershipWrites: wrapper);
        var original = (await data.Members.FindStateAsync(data.Tenant, data.Member))!;
        using var rejected = await Write(f, data, data.Member, new { policyKey = "default", roleIds = Array.Empty<Guid>() },
            "role-replacement-native-race", original.Version, true);
        Assert.AreEqual(HttpStatusCode.PreconditionFailed, rejected.StatusCode);
        var current = (await data.Members.FindStateAsync(data.Tenant, data.Member))!;
        Assert.Contains(data.Owner.RoleId, current.RoleIds); Assert.Contains(data.MemberRole.RoleId, current.RoleIds);
        Assert.AreNotEqual(original.Version, current.Version);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task ConcurrentSignedMutationsForDifferentAdminsHaveOneWinner()
    {
        await using var data = await MembershipStorageFixture.Create(); await data.Members.ConfigureProtectionAsync(data.Tenant, [data.Owner.RoleId]);
        var a = (await data.Members.FindStateAsync(data.Tenant, data.AdminA))!; var b = (await data.Members.FindStateAsync(data.Tenant, data.AdminB))!;
        await using var f = await DelegationFixture.Start(memberships: data, membershipWrites: new SnapshotBarrier(data.Members));
        var responses = await Task.WhenAll(Write(f, data, data.AdminA, new { isEnabled = false }, "membership-admin-first", a.Version),
            Write(f, data, data.AdminB, new { isEnabled = false }, "membership-admin-second", b.Version));
        try { Assert.AreEqual(1, responses.Count(x => x.StatusCode == HttpStatusCode.OK)); Assert.IsTrue(responses.All(x => x.StatusCode is HttpStatusCode.OK or HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)); }
        finally { foreach (var response in responses) response.Dispose(); }
        var snapshot = await data.Members.ReadSnapshotAsync(data.Tenant, 5000); Assert.AreEqual(1, snapshot.Members.Count(x => x.IsEnabled && x.RoleIds.Contains(data.Owner.RoleId)));
    }
}
