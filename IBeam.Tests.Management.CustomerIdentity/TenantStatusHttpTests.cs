using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Data.Tables;
using IBeam.Identity.Exceptions;
using IBeam.Management.CustomerIdentity;
namespace IBeam.Tests.Management.CustomerIdentity;
[TestClass]
public sealed class TenantStatusHttpTests
{
    private static async Task<HttpResponseMessage> Write(DelegationFixture f, Guid tenant, bool active, string version, string key, byte[]? body = null)
    {
        var path = $"/api/management-identity/tenants/{tenant}/" + (active ? "activate" : "deactivate"); body ??= Encoding.UTF8.GetBytes("{}");
        return await f.Send(await f.Issue(ManagementCustomerIdentityOperations.TenantsStatus, "POST", path, body, key, version), "POST", path, body, key, version);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task StatusPreservesIdentityDataAndDisabledMembershipsAndUsesAnIndependentLiveGrant()
    {
        await using var data = await MembershipStorageFixture.Create();
        var a = (await data.Members.FindStateAsync(data.Tenant, data.AdminA))!;
        await data.Administration().SetEnabledAsync(data.Tenant, data.AdminA, false, a.Version, Guid.NewGuid(), [data.Owner.RoleId]);
        var disabledMember = (await data.Members.FindStateAsync(data.Tenant, data.AdminA))!;
        var other = (await data.Tenants.FindVersionedAsync(data.OtherTenant))!;
        var row = (await data.TenantTable.GetEntityAsync<TableEntity>("TEN", data.Tenant.ToString("D"))).Value;
        row["ConsumerProfileJson"] = "PRIVATE-CUSTOMER-CANARY"; await data.TenantTable.UpdateEntityAsync(row, row.ETag, TableUpdateMode.Merge);
        var tenant = (await data.Tenants.FindVersionedAsync(data.Tenant))!;
        await using var f = await DelegationFixture.Start(memberships: data, tenantStatus: true);
        using var response = await Write(f, data.Tenant, false, tenant.Version!, "tenant-disable-first");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync()); Assert.DoesNotContain("PRIVATE-CUSTOMER", await response.Content.ReadAsStringAsync());
        var current = (await data.Tenants.FindVersionedAsync(data.Tenant))!; Assert.IsFalse(current.Tenant.IsActive);
        using var repeated = await Write(f, data.Tenant, false, tenant.Version!, "tenant-disable-first"); Assert.AreEqual(HttpStatusCode.OK, repeated.StatusCode);
        Assert.AreEqual(current.Version, (await data.Tenants.FindVersionedAsync(data.Tenant))!.Version);
        using var stale = await Write(f, data.Tenant, true, tenant.Version!, "tenant-activate-stale"); Assert.AreEqual(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        using var activated = await Write(f, data.Tenant, true, current.Version!, "tenant-activate-first"); Assert.AreEqual(HttpStatusCode.OK, activated.StatusCode);
        Assert.IsFalse((await data.Members.FindStateAsync(data.Tenant, data.AdminA))!.IsEnabled); Assert.AreEqual(disabledMember.Version, (await data.Members.FindStateAsync(data.Tenant, data.AdminA))!.Version);
        Assert.AreEqual(other.Version, (await data.Tenants.FindVersionedAsync(data.OtherTenant))!.Version);
        Assert.AreEqual("PRIVATE-CUSTOMER-CANARY", (await data.TenantTable.GetEntityAsync<TableEntity>("TEN", data.Tenant.ToString("D"))).Value.GetString("ConsumerProfileJson"));
        f.Credentials.Row = f.Credentials.Row with { Capabilities = f.Credentials.Row.Capabilities.Where(x => x != ManagementCustomerIdentityOperations.TenantsStatus).ToArray() };
        using var denied = await Write(f, data.Tenant, false, (await data.Tenants.FindVersionedAsync(data.Tenant))!.Version!, "tenant-status-denied"); Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task RequiredStatusHookRecoveryDoesNotWriteTenantAgainAndDisabledTenantRejectsRoleChanges()
    {
        await using var data = await MembershipStorageFixture.Create(); await using var f = await DelegationFixture.Start(memberships: data, tenantStatus: true);
        var original = (await data.Tenants.FindVersionedAsync(data.Tenant))!; f.TenantHooks.FailStatus = true;
        using var failed = await Write(f, data.Tenant, false, original.Version!, "tenant-status-recover"); Assert.AreEqual(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        var applied = (await data.Tenants.FindVersionedAsync(data.Tenant))!; Assert.IsFalse(applied.Tenant.IsActive); f.TenantHooks.FailStatus = false;
        using var recovered = await Write(f, data.Tenant, false, original.Version!, "tenant-status-recover"); Assert.AreEqual(HttpStatusCode.OK, recovered.StatusCode, await recovered.Content.ReadAsStringAsync());
        Assert.AreEqual(applied.Version, (await data.Tenants.FindVersionedAsync(data.Tenant))!.Version);
        var member = (await data.Members.FindStateAsync(data.Tenant, data.Member))!; var path = $"/api/management-identity/tenants/{data.Tenant}/users/{data.Member}/roles";
        var body = JsonSerializer.SerializeToUtf8Bytes(new { policyKey = "default", roleIds = Array.Empty<Guid>() });
        using var denied = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.RolesAssign, "PUT", path, body, ifMatch: member.Version), "PUT", path, body, ifMatch: member.Version);
        Assert.AreEqual(HttpStatusCode.Conflict, denied.StatusCode); Assert.AreEqual(member.Version, (await data.Members.FindStateAsync(data.Tenant, data.Member))!.Version);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task ConcurrentActivateAndDeactivateUseOneVersionAndUnknownFieldsNeverReachNativeStatus()
    {
        await using var data = await MembershipStorageFixture.Create(); await using var f = await DelegationFixture.Start(memberships: data, tenantStatus: true);
        var tenant = (await data.Tenants.FindVersionedAsync(data.Tenant))!;
        var results = await Task.WhenAll(Write(f, data.Tenant, false, tenant.Version!, "tenant-race-disable"), Write(f, data.Tenant, true, tenant.Version!, "tenant-race-activate"));
        try { Assert.AreEqual(1, results.Count(x => x.StatusCode == HttpStatusCode.OK)); Assert.IsTrue(results.All(x => x.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)); }
        finally { foreach (var response in results) response.Dispose(); }
        var current = (await data.Tenants.FindVersionedAsync(data.Tenant))!;
        using var bad = await Write(f, data.Tenant, true, current.Version!, "tenant-private-input", Encoding.UTF8.GetBytes("{\"reason\":\"PRIVATE-PROFILE-CANARY\"}"));
        Assert.AreEqual(HttpStatusCode.BadRequest, bad.StatusCode); Assert.DoesNotContain("PRIVATE-PROFILE", await bad.Content.ReadAsStringAsync());
        Assert.AreEqual(current.Version, (await data.Tenants.FindVersionedAsync(data.Tenant))!.Version);
    }
}
