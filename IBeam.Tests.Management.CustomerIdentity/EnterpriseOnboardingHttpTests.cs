using System.Net;
using System.Text.Json;
using System.Text;
using IBeam.Identity.Models;
using IBeam.Management.CustomerIdentity;
using Moq;
namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class EnterpriseOnboardingHttpTests
{
    [TestMethod]
    public async Task PolicyDiscoveryRequiresSignedOnboardingReadAndDoesNotExposeImplementation()
    {
        var data = new OnboardingFixture(); await using var f = await DelegationFixture.Start(onboarding: data);
        var path = "/api/management-identity/enterprise-onboardings/policies";
        using var response = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.OnboardingRead, path: path), path: path);
        var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.Contains("enterprise-owner", text); Assert.DoesNotContain("Owner", text); Assert.AreEqual(0, data.Sends);
        using var wrong = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.UsersRead, path: path), path: path);
        Assert.AreNotEqual(HttpStatusCode.OK, wrong.StatusCode);
    }
    private const string Path = "/api/management-identity/enterprise-onboardings";
    private static byte[] Body(OnboardingFixture data) => JsonSerializer.SerializeToUtf8Bytes(data.Request, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static async Task<ManagementIdentityOperationDto> Parse(HttpResponseMessage response, HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected, response.StatusCode, text);
        Assert.DoesNotContain("enterprise@example.test", text); Assert.DoesNotContain("Private Enterprise", text);
        using var document = JsonDocument.Parse(text);
        return document.RootElement.GetProperty("data").Deserialize<ManagementIdentityOperationDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
    private static Task<HttpResponseMessage> Create(DelegationFixture f, OnboardingFixture data)
        => Send(f, ManagementCustomerIdentityOperations.OnboardingCreate, Path, Body(data));
    private static async Task<HttpResponseMessage> Send(DelegationFixture f, string operation, string path, byte[] body, string key = "onboard-http-intent")
        => await f.Send(await f.Issue(operation, "POST", path, body, key), "POST", path, body, key);
    private static async Task<HttpResponseMessage> Get(DelegationFixture f, Guid id)
    {
        var path = "/api/management-identity/operations/" + id;
        return await f.Send(await f.Issue(ManagementCustomerIdentityOperations.OnboardingRead, path: path), path: path);
    }
    [TestMethod]
    public async Task SignedCreateAndReadReuseOneReceiptAndExposeBusinessStatusWithoutTokensOrAutomaticCustomerAccess()
    {
        var data = new OnboardingFixture(); await using var f = await DelegationFixture.Start(onboarding: data);
        using var created = await Create(f, data); var a = await Parse(created, HttpStatusCode.Accepted);
        using var repeat = await Create(f, data); var b = await Parse(repeat, HttpStatusCode.Accepted);
        Assert.AreEqual(a.OperationId, b.OperationId); Assert.AreEqual(a.ResourceId, b.ResourceId);
        Assert.AreEqual("succeeded", a.Status); Assert.AreEqual("awaiting-acceptance", a.Onboarding!.Status); Assert.IsFalse(a.Onboarding.Completed);
        Assert.AreEqual("await-customer", a.Onboarding.NextAction); Assert.AreEqual(1, data.Sends); Assert.HasCount(1, data.InviteRows);
        using var polled = await Get(f, a.OperationId); var read = await Parse(polled, HttpStatusCode.OK);
        Assert.AreEqual(a.Onboarding, read.Onboarding); Assert.AreEqual(1, data.Sends); Assert.HasCount(0, data.MembershipCalls);
        var audits = JsonSerializer.Serialize(f.Audit.Rows); Assert.DoesNotContain(data.LastRequest!.Token, audits); Assert.DoesNotContain(data.Request.InitialAdminEmail, audits);
        Assert.IsTrue(f.Audit.Rows.Any(x => x.Operation == ManagementCustomerIdentityOperations.OnboardingCreate && x.ActorUserId == f.User));
    }
    [TestMethod]
    public async Task ExplicitRetryAfterUnknownDeliveryUsesOriginalOperationAndCurrentCompositeAuthority()
    {
        var data = new OnboardingFixture { FailDelivery = true }; await using var f = await DelegationFixture.Start(onboarding: data);
        using var created = await Create(f, data); var original = await Parse(created, HttpStatusCode.Accepted);
        Assert.AreEqual("unknown", original.Onboarding!.DeliveryStatus);
        data.FailDelivery = false; var path = Path + "/" + original.OperationId + "/retry"; var body = Encoding.UTF8.GetBytes("{}");
        using var retried = await Send(f, ManagementCustomerIdentityOperations.OnboardingRetry, path, body, "onboard-retry-intent");
        var retry = await Parse(retried, HttpStatusCode.Accepted);
        using var repeated = await Send(f, ManagementCustomerIdentityOperations.OnboardingRetry, path, body, "onboard-retry-intent");
        var repeat = await Parse(repeated, HttpStatusCode.Accepted);
        Assert.AreEqual(retry.OperationId, repeat.OperationId); Assert.AreEqual(original.ResourceId, retry.ResourceId);
        Assert.AreEqual(original.Onboarding.InviteId, retry.Onboarding!.InviteId); Assert.AreEqual(2, data.Sends); Assert.HasCount(1, data.InviteRows);
        f.Credentials.Row = f.Credentials.Row with { Capabilities = f.Credentials.Row.Capabilities.Where(x => x != ManagementCustomerIdentityOperations.RolesAssign).ToArray() };
        using var denied = await Send(f, ManagementCustomerIdentityOperations.OnboardingRetry, path, body, "onboard-retry-intent-2");
        Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode); Assert.AreEqual(2, data.Sends);
        using var stillReadable = await Get(f, original.OperationId); await Parse(stillReadable, HttpStatusCode.OK);
    }
    [TestMethod]
    [DataRow("new")] [DataRow("existing")]
    public async Task AuthoritativeVerifiedMembershipCompletesOnboardingAndCompletedRetryDoesNotCreateOrSend(string customerType)
    {
        var data = new OnboardingFixture(); await using var f = await DelegationFixture.Start(onboarding: data);
        using var created = await Create(f, data); var receipt = await Parse(created, HttpStatusCode.Accepted);
        var invite = data.InviteRows.Values.Single(); var user = Guid.NewGuid();
        data.InviteRows[invite.InviteId] = invite with { Status = "redeemed", RedeemedByUserId = user, RedeemedUtc = DateTimeOffset.UtcNow };
        data.Users.Setup(x => x.FindByIdAsync(user, It.IsAny<CancellationToken>())).ReturnsAsync(new IdentityUser(user, data.Request.InitialAdminEmail, true));
        data.Memberships.Setup(x => x.GetTenantForUserAsync(user, invite.TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(new TenantInfo(invite.TenantId, customerType, ["Owner"], true, [data.RoleId]));
        using var read = await Get(f, receipt.OperationId); var completed = await Parse(read, HttpStatusCode.OK);
        Assert.IsTrue(completed.Onboarding!.Completed); Assert.AreEqual("none", completed.Onboarding.NextAction);
        var rows = data.Jobs.Rows.Count;
        using var repeat = await Send(f, ManagementCustomerIdentityOperations.OnboardingRetry, Path + "/" + receipt.OperationId + "/retry", Encoding.UTF8.GetBytes("{}"), "completed-retry-intent");
        var unchanged = await Parse(repeat, HttpStatusCode.OK); Assert.IsTrue(unchanged.Onboarding!.Completed);
        Assert.AreEqual(1, data.Sends); Assert.HasCount(1, data.InviteRows); Assert.AreEqual(rows, data.Jobs.Rows.Count);
    }
    [TestMethod]
    [DataRow("revoked")] [DataRow("expired")] [DataRow("wrong-recipient")]
    public async Task RevokedExpiredOrWrongRecipientNeverCompletes(string state)
    {
        var data = new OnboardingFixture(); await using var f = await DelegationFixture.Start(onboarding: data);
        using var created = await Create(f, data); var receipt = await Parse(created, HttpStatusCode.Accepted);
        var invite = data.InviteRows.Values.Single(); var user = Guid.NewGuid();
        data.InviteRows[invite.InviteId] = invite with { Status = state == "wrong-recipient" ? "redeemed" : state, RedeemedByUserId = user };
        data.Users.Setup(x => x.FindByIdAsync(user, It.IsAny<CancellationToken>())).ReturnsAsync(new IdentityUser(user, "wrong@example.test", true));
        using var read = await Get(f, receipt.OperationId); var result = await Parse(read, HttpStatusCode.OK);
        Assert.IsFalse(result.Onboarding!.Completed); Assert.AreEqual(1, data.Sends);
    }
    [TestMethod]
    public async Task InvalidPolicyExtraRetryFieldsAndCrossApplicationOperationIdsCannotMutate()
    {
        var data = new OnboardingFixture(); await using var f = await DelegationFixture.Start(onboarding: data);
        using var invalid = await Send(f, ManagementCustomerIdentityOperations.OnboardingCreate, Path, Encoding.UTF8.GetBytes("{\"tenantName\":\"Name\",\"initialAdminEmail\":\"admin@example.test\",\"policyKey\":\"support-*\"}"));
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode); Assert.HasCount(0, data.Jobs.Rows);
        using var created = await Create(f, data); var receipt = await Parse(created, HttpStatusCode.Accepted);
        var path = Path + "/" + receipt.OperationId + "/retry";
        using var extra = await Send(f, ManagementCustomerIdentityOperations.OnboardingRetry, path, Encoding.UTF8.GetBytes("{\"initialAdminEmail\":\"attacker@example.test\"}"));
        Assert.AreEqual(HttpStatusCode.BadRequest, extra.StatusCode); Assert.AreEqual(1, data.Sends);
        await using var other = await DelegationFixture.Start(onboarding: data);
        using var cross = await Get(other, receipt.OperationId); Assert.AreEqual(HttpStatusCode.NotFound, cross.StatusCode);
        Assert.AreEqual(1, data.Sends);
    }
}
