using System.Net;
using System.Text;
using System.Text.Json;
using IBeam.Identity.Interfaces;
using IBeam.Management.CustomerIdentity;
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
    public async Task LostProjectionReplyReconcilesThroughOneDurableOperationWithoutAnotherPrimaryWrite()
    {
        await using var data = await MembershipStorageFixture.Create(); var wrapper = new SnapshotBarrier(data.Members) { WaitForPair = false, FailProjectionOnce = true };
        await using var f = await DelegationFixture.Start(memberships: data, membershipWrites: wrapper);
        var original = (await data.Members.FindStateAsync(data.Tenant, data.Member))!;
        using var failed = await Write(f, data, data.Member, new { isEnabled = false }, "membership-recovery-intent", original.Version); Assert.AreEqual(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        var applied = (await data.Members.FindStateAsync(data.Tenant, data.Member))!; Assert.IsFalse(applied.IsEnabled);
        using var recovered = await Write(f, data, data.Member, new { isEnabled = false }, "membership-recovery-intent", original.Version);
        Assert.AreEqual(HttpStatusCode.OK, recovered.StatusCode, await recovered.Content.ReadAsStringAsync()); Assert.AreEqual(applied.Version, (await data.Members.FindStateAsync(data.Tenant, data.Member))!.Version);
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
