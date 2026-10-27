using System.Net;
using System.Text.Json;
using System.Text;
using IBeam.Management.CustomerIdentity;
namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class CustomerProvisioningHttpTests
{
    [TestMethod]
    public async Task SignedCatalogAndProvisionUseSeparateGrantsAndNeverLinkCustomerMembershipBeforeAcceptance()
    {
        var data = new ProvisioningFixture(); await using var f = await DelegationFixture.Start(provisioning: data);
        var rolesPath = "/api/management-identity/tenants/" + data.Tenant + "/assignable-roles";
        using var roles = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.RolesRead, path: rolesPath), path: rolesPath);
        Assert.AreEqual(HttpStatusCode.OK, roles.StatusCode); var text = await roles.Content.ReadAsStringAsync(); Assert.Contains("Member", text); Assert.DoesNotContain("Support", text);
        var path = "/api/management-identity/tenants/" + data.Tenant + "/users/provision";
        var body = JsonSerializer.SerializeToUtf8Bytes(data.Request, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        async Task<HttpResponseMessage> Send(string key) => await f.Send(await f.Issue(ManagementCustomerIdentityOperations.UsersProvision, "POST", path, body, key), "POST", path, body, key);
        using var created = await Send("provision-http-intent"); Assert.AreEqual(HttpStatusCode.Accepted, created.StatusCode, await created.Content.ReadAsStringAsync());
        using var repeated = await Send("provision-http-intent"); Assert.AreEqual(HttpStatusCode.Accepted, repeated.StatusCode);
        using var differentKey = await Send("provision-http-intent-2"); Assert.AreEqual(HttpStatusCode.Accepted, differentKey.StatusCode);
        Assert.HasCount(1, data.Data.InviteRows); Assert.AreEqual(1, data.Data.Sends);
        using var result = JsonDocument.Parse(await created.Content.ReadAsStringAsync()); var status = result.RootElement.GetProperty("data");
        Assert.AreEqual("sent", status.GetProperty("status").GetString()); Assert.IsNotNull(created.Headers.Location);
        Assert.DoesNotContain(data.Data.LastRequest!.Token, await created.Content.ReadAsStringAsync());
        f.Credentials.Row = f.Credentials.Row with { Capabilities = f.Credentials.Row.Capabilities.Where(x => x != ManagementCustomerIdentityOperations.RolesAssign).ToArray() };
        using var stillRead = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.RolesRead, path: rolesPath), path: rolesPath); Assert.AreEqual(HttpStatusCode.OK, stillRead.StatusCode);
        using var denied = await Send("provision-http-intent-3"); Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode); Assert.AreEqual(1, data.Data.Sends);
        Assert.DoesNotContain(data.Request.Email, JsonSerializer.Serialize(f.Audit.Rows));
    }
    [TestMethod]
    public async Task ArbitraryRolesRedirectsAndOtherTenantIdsAreRejectedWithoutInvitationWrites()
    {
        var data = new ProvisioningFixture(); await using var f = await DelegationFixture.Start(provisioning: data);
        var path = "/api/management-identity/tenants/" + data.Tenant + "/users/provision";
        var bad = Encoding.UTF8.GetBytes("{\"email\":\"customer@example.test\",\"policyKey\":\"default\",\"roleIds\":[],\"redirectUrl\":\"https://evil.example\"}");
        using var denied = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.UsersProvision, "POST", path, bad), "POST", path, bad);
        Assert.AreEqual(HttpStatusCode.BadRequest, denied.StatusCode); Assert.DoesNotContain("evil.example", await denied.Content.ReadAsStringAsync());
        var missingPath = "/api/management-identity/tenants/" + Guid.NewGuid() + "/users/provision";
        var body = JsonSerializer.SerializeToUtf8Bytes(data.Request, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var missing = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.UsersProvision, "POST", missingPath, body), "POST", missingPath, body);
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode); Assert.HasCount(0, data.Data.InviteRows);
    }
    [TestMethod]
    [DataRow("redeemed")] [DataRow("expired")] [DataRow("revoked")]
    public async Task ProvisionReceiptPollingReadsCurrentNativeStateWithoutDecryptingOrSending(string state)
    {
        var data = new ProvisioningFixture(); await using var f = await DelegationFixture.Start(provisioning: data);
        var path = "/api/management-identity/tenants/" + data.Tenant + "/users/provision";
        var body = JsonSerializer.SerializeToUtf8Bytes(data.Request, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var created = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.UsersProvision, "POST", path, body), "POST", path, body);
        Assert.AreEqual(HttpStatusCode.Accepted, created.StatusCode);
        using var initial = JsonDocument.Parse(await created.Content.ReadAsStringAsync()); var id = initial.RootElement.GetProperty("data").GetProperty("operation").GetProperty("operationId").GetGuid();
        var native = data.Data.InviteRows.Values.Single(); data.Data.InviteRows[native.InviteId] = native with { Status = state, Version = "fixture-invite-version" };
        data.Journal.Rows[data.Journal.Rows.Keys.Single()] = data.Journal.Rows.Values.Single() with { EncryptedInput = "Intentionally not decryptable: query needs metadata only" };
        var pollPath = "/api/management-identity/operations/" + id;
        using var poll = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.UsersProvision, path: pollPath), path: pollPath);
        Assert.AreEqual(HttpStatusCode.OK, poll.StatusCode, await poll.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await poll.Content.ReadAsStringAsync()); var invitation = json.RootElement.GetProperty("data").GetProperty("invitation");
        Assert.AreEqual(state == "redeemed" ? "accepted" : state, invitation.GetProperty("status").GetString());
        Assert.AreEqual(data.Tenant, invitation.GetProperty("tenantId").GetGuid()); Assert.AreEqual(native.InviteId, invitation.GetProperty("inviteId").GetGuid());
        Assert.DoesNotContain(data.Request.Email, await poll.Content.ReadAsStringAsync()); Assert.DoesNotContain(data.Data.LastRequest!.Token, await poll.Content.ReadAsStringAsync());
        Assert.AreEqual(1, data.Data.Sends);
        f.Credentials.Row = f.Credentials.Row with { Status = "Revoked", RevokedUtc = DateTimeOffset.UtcNow };
        using var denied = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.UsersProvision, path: pollPath), path: pollPath);
        Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode);
    }
}
