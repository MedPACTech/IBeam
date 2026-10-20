using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IBeam.Management.CustomerIdentity;
namespace IBeam.Tests.Management.Applications;
[TestClass]
public sealed class PortalCustomerMembershipTests
{
    private static readonly string[] Grants = [ManagementCustomerIdentityOperations.MembershipsManage, ManagementCustomerIdentityOperations.RolesAssign];
    private static Task<Guid> App(PortalHttpFixture f, string name = "qurvia", string[]? grants = null) => f.Directories.AddApp(f, name, grants ?? Grants, includeMemberships: true);
    private static async Task<HttpResponseMessage> Write(PortalHttpFixture f, Guid app, object input, string version = "member-v1", string key = "portal-member-intent", bool roles = false)
    {
        var path = $"/api/management/applications/{app}/tenants/{f.Directories.Tenant}/users/{f.Directories.User}/" + (roles ? "roles" : "membership");
        using var request = new HttpRequestMessage(roles ? HttpMethod.Put : HttpMethod.Patch, path);
        request.Headers.Add("Authorization", "Bearer reader"); request.Headers.Add("Idempotency-Key", key); request.Headers.TryAddWithoutValidation("If-Match", version);
        request.Content = JsonContent.Create(input); return await f.Client.SendAsync(request);
    }
    private static async Task<ManagementPortalCustomerMembershipResult> Result(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(status, response.StatusCode, text); Assert.DoesNotContain(CustomerDirectoryBackend.Canary, text);
        using var json = JsonDocument.Parse(text); return json.RootElement.GetProperty("data").Deserialize<ManagementPortalCustomerMembershipResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
    [TestMethod]
    public async Task StatusAndRolesRemainIndependentlyGrantedAndVersionsArePreserved()
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerMemberships: true, audit: true);
        var app = await App(f, grants: [ManagementCustomerIdentityOperations.MembershipsManage]);
        using var disabled = await Write(f, app, new { isEnabled = false }); var result = await Result(disabled); Assert.AreEqual("disabled", result.Membership!.Status);
        using var repeated = await Write(f, app, new { isEnabled = false }); await Result(repeated); Assert.AreEqual(1, f.Directories.MembershipWrites);
        using var stale = await Write(f, app, new { isEnabled = true }, key: "portal-member-stale"); Assert.AreEqual(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        using var roleDenied = await Write(f, app, new { policyKey = "default", roleIds = Array.Empty<Guid>() }, result.Membership.Version!, roles: true); Assert.AreEqual(HttpStatusCode.Forbidden, roleDenied.StatusCode);
        var other = await App(f, "bindry", [ManagementCustomerIdentityOperations.RolesAssign]);
        using var role = await Write(f, other, new { policyKey = "default", roleIds = Array.Empty<Guid>() }, roles: true); var cleared = await Result(role); Assert.HasCount(0, cleared.Membership!.RoleIds);
        using var statusDenied = await Write(f, other, new { isEnabled = false }, cleared.Membership.Version!); Assert.AreEqual(HttpStatusCode.Forbidden, statusDenied.StatusCode);
        Assert.AreEqual(2, f.Directories.MembershipWrites); Assert.DoesNotContain(CustomerDirectoryBackend.Canary, JsonSerializer.Serialize(f.Audit.Rows));
        Assert.IsTrue(f.Audit.Rows.Any(x => x.Operation.EndsWith("memberships.manage.succeeded", StringComparison.Ordinal)));
    }
    [TestMethod]
    [DataRow("after-commit-timeout")] [DataRow("after-commit-malformed")] [DataRow("after-commit-503")]
    public async Task LostRepliesPollOneExistingOperationWithoutRepeatingMembershipWrites(string failure)
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerMemberships: true); var app = await App(f); f.Directories.Failure = failure;
        using var response = await Write(f, app, new { isEnabled = false }); var unknown = await Result(response, HttpStatusCode.Accepted);
        Assert.AreEqual("unknown", unknown.Tracking.Status); Assert.IsNull(unknown.Membership); Assert.AreEqual(1, f.Directories.MembershipWrites);
        f.Directories.Failure = null;
        using var poll = await f.Get(response.Headers.Location!.ToString()); Assert.AreEqual(HttpStatusCode.OK, poll.StatusCode); Assert.AreEqual(1, f.Directories.MembershipWrites);
        Assert.Contains("\"succeeded\"", await poll.Content.ReadAsStringAsync());
        var other = await App(f, "bindry"); using var cross = await f.Get(response.Headers.Location.ToString().Replace(app.ToString(), other.ToString())); Assert.AreEqual(HttpStatusCode.BadRequest, cross.StatusCode);
    }
    [TestMethod]
    public async Task EmptyRoleAssignmentWhileDisabledDoesNotReenableAndReenableDoesNotRestoreRemovedRoles()
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerMemberships: true); var app = await App(f);
        using var disabled = await Write(f, app, new { isEnabled = false }); var a = await Result(disabled);
        using var removed = await Write(f, app, new { policyKey = "default", roleIds = Array.Empty<Guid>() }, a.Membership!.Version!, key: "portal-member-roles", roles: true); var b = await Result(removed);
        Assert.AreEqual("disabled", b.Membership!.Status); Assert.HasCount(0, b.Membership.RoleIds);
        using var enabled = await Write(f, app, new { isEnabled = true }, b.Membership.Version!, key: "portal-member-enable"); var c = await Result(enabled);
        Assert.AreEqual("active", c.Membership!.Status); Assert.HasCount(0, c.Membership.RoleIds);
    }
    [TestMethod]
    public async Task ProfileAndReasonOverridesUnapprovedRolesAndPermissionLossFailSafely()
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerMemberships: true); var app = await App(f);
        using var extra = await Write(f, app, new { isEnabled = false, reason = CustomerDirectoryBackend.Canary }); Assert.AreEqual(HttpStatusCode.BadRequest, extra.StatusCode);
        using var role = await Write(f, app, new { policyKey = "default", roleIds = new[] { Guid.NewGuid() } }, roles: true); Assert.AreEqual(HttpStatusCode.Forbidden, role.StatusCode);
        Assert.AreEqual(0, f.Directories.MembershipWrites);
        var identity = (f.Data.Access.Org, app, f.Data.Access.Reader); var old = f.Data.Access.Access.Rows[identity];
        f.Directories.DuringMutation = _ => { f.Data.Access.Access.Rows[identity] = old with { Permissions = [] }; return Task.CompletedTask; };
        using var denied = await Write(f, app, new { isEnabled = false }); Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode); Assert.AreEqual(1, f.Directories.MembershipWrites);
    }
}
