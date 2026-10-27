using System.Net;
using System.Text;
using IBeam.Management.CustomerIdentity;
namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class CustomerInvitationLifecycleHttpTests
{
    [TestMethod]
    public async Task SignedReadAndResendUseIndependentGrantsBoundHeadersAndSafeErrors()
    {
        var data = new InvitationLifecycleFixture(); await using var f = await DelegationFixture.Start(invitations: data);
        var path = $"/api/management-identity/tenants/{data.Tenant}/invites/{data.Id}";
        using var read = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.InvitesRead, path: path), path: path);
        Assert.AreEqual(HttpStatusCode.OK, read.StatusCode, await read.Content.ReadAsStringAsync());
        var body = Encoding.UTF8.GetBytes("{}"); var version = data.Row.Version!;
        async Task<HttpResponseMessage> Send(string key, string currentVersion) => await f.Send(await f.Issue(ManagementCustomerIdentityOperations.InvitesManage,
            "POST", path + "/resend", body, key, currentVersion), "POST", path + "/resend", body, key, currentVersion);
        using var first = await Send("http-resend-first-intent", version); Assert.AreEqual(HttpStatusCode.OK, first.StatusCode, await first.Content.ReadAsStringAsync());
        using var repeated = await Send("http-resend-first-intent", version); Assert.AreEqual(HttpStatusCode.OK, repeated.StatusCode); Assert.AreEqual(1, data.Sends);
        var operationPath = first.Headers.Location!.ToString();
        using var operation = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.InvitesManage, path: operationPath), path: operationPath);
        Assert.AreEqual(HttpStatusCode.OK, operation.StatusCode); Assert.Contains("\"deliveryStatus\":\"sent\"", await operation.Content.ReadAsStringAsync()); Assert.AreEqual(1, data.Sends);
        using var stale = await Send("http-resend-second-intent", version); Assert.AreEqual(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        using var limited = await Send("http-resend-third-intent", data.Row.Version!); Assert.AreEqual(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.DoesNotContain(data.LastToken!, await first.Content.ReadAsStringAsync());
        f.Credentials.Row = f.Credentials.Row with { Capabilities = f.Credentials.Row.Capabilities.Where(x => x != ManagementCustomerIdentityOperations.InvitesManage).ToArray() };
        using var denied = await Send("http-resend-fourth-intent", data.Row.Version!); Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var stillRead = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.InvitesRead, path: path), path: path); Assert.AreEqual(HttpStatusCode.OK, stillRead.StatusCode);
    }
    [TestMethod]
    public async Task RecipientRoleAndRedirectOverridesAndOtherTenantScopesAreRejected()
    {
        var data = new InvitationLifecycleFixture(); await using var f = await DelegationFixture.Start(invitations: data);
        var path = $"/api/management-identity/tenants/{data.Tenant}/invites/{data.Id}/revoke";
        var body = Encoding.UTF8.GetBytes("{\"email\":\"other@example.test\",\"redirectUrl\":\"https://evil.example\"}");
        using var invalid = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.InvitesManage, "POST", path, body, ifMatch: data.Row.Version), "POST", path, body, ifMatch: data.Row.Version);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode); Assert.DoesNotContain("evil.example", await invalid.Content.ReadAsStringAsync());
        var wrong = path.Replace(data.Tenant.ToString(), Guid.NewGuid().ToString()); body = Encoding.UTF8.GetBytes("{}");
        using var missing = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.InvitesManage, "POST", wrong, body, ifMatch: data.Row.Version), "POST", wrong, body, ifMatch: data.Row.Version);
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode); Assert.AreEqual(0, data.Sends); Assert.HasCount(0, data.Journal.Rows);
    }
}
