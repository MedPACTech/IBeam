using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IBeam.Management.CustomerIdentity;
namespace IBeam.Tests.Management.Applications;

[TestClass]
public sealed class PortalEnterpriseOnboardingTests
{
    [TestMethod]
    public async Task PolicyDiscoveryIsAppBoundSignedAndDropsTargetPrivateFields()
    {
        await using var f = await PortalHttpFixture.CreateAsync(customerOnboarding: true);
        var app = await App(f);
        using var response = await f.Get(Path(app) + "/policies");
        var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.Contains("enterprise-owner", text); Assert.DoesNotContain(CustomerDirectoryBackend.Canary, text);
        using var anonymous = await f.Get(Path(app) + "/policies", token: "");
        Assert.AreNotEqual(HttpStatusCode.OK, anonymous.StatusCode);
    }
    private static readonly string[] Grants = ManagementCustomerIdentityPermissionPolicy.RequiredFor(ManagementCustomerIdentityOperations.OnboardingRetry)
        .Concat(new[] { ManagementCustomerIdentityOperations.OnboardingRead }).ToArray();
    private static string Path(Guid app) => "/api/management/applications/" + app + "/enterprise-onboardings";
    private static Task<Guid> App(PortalHttpFixture f, string name = "qurvia", string[]? grants = null) => f.Directories.AddApp(f, name, grants ?? Grants, includeOnboarding: true);
    private static async Task<HttpResponseMessage> Write(PortalHttpFixture f, Guid app, Guid? retry = null, string name = "Enterprise", string key = "portal-onboard-intent", string token = "reader", bool extra = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Path(app) + (retry is Guid id ? "/" + id + "/retry" : ""));
        request.Headers.Add("Authorization", "Bearer " + token); request.Headers.Add("Idempotency-Key", key);
        request.Content = retry is not null ? JsonContent.Create(new { }) : extra ? JsonContent.Create(new { tenantName = name, initialAdminEmail = "admin@example.test", policyKey = "enterprise-owner", profile = CustomerDirectoryBackend.Canary }) :
            JsonContent.Create(new CreateManagementEnterpriseOnboarding { TenantName = name, InitialAdminEmail = "admin@example.test", PolicyKey = "enterprise-owner" });
        return await f.Client.SendAsync(request);
    }
    private static async Task<ManagementPortalOperationResult> Parse(HttpResponseMessage response, HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected, response.StatusCode, text);
        Assert.DoesNotContain(CustomerDirectoryBackend.Canary, text); Assert.DoesNotContain("admin@example.test", text);
        using var doc = JsonDocument.Parse(text); return doc.RootElement.GetProperty("data").Deserialize<ManagementPortalOperationResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
    [TestMethod]
    public async Task TwoSelectedAppsCanHaveOverlappingOperationIdsWithoutSharingTheirConnectionsOrReferences()
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerOnboarding: true, audit: true);
        var app = await App(f); var other = await App(f, "bindry");
        using var a = await Write(f, app); var first = await Parse(a, HttpStatusCode.Accepted);
        using var b = await Write(f, other); var second = await Parse(b, HttpStatusCode.Accepted);
        Assert.AreEqual(first.Operation!.OperationId, second.Operation!.OperationId);
        Assert.AreEqual("awaiting-acceptance", first.Status); Assert.IsFalse(first.Operation.Onboarding!.Completed);
        Assert.AreNotEqual(first.Reference, second.Reference); Assert.AreEqual(2, f.Directories.OnboardingCreates);
        var crossPath = a.Headers.Location!.ToString().Replace(app.ToString(), other.ToString());
        using var cross = await f.Get(crossPath); Assert.AreEqual(HttpStatusCode.BadRequest, cross.StatusCode);
        using var repeated = await Write(f, app); await Parse(repeated, HttpStatusCode.Accepted);
        Assert.AreEqual(2, f.Directories.OnboardingCreates); Assert.AreEqual(2, f.Directories.OnboardingDeliveries);
        using var changed = await Write(f, app, name: "Different enterprise"); Assert.AreEqual(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.DoesNotContain("admin@example.test", JsonSerializer.Serialize(f.Audit.Rows));
        Assert.IsTrue(f.Audit.Rows.Any(x => x.Operation.EndsWith("onboarding.create.awaiting-acceptance", StringComparison.Ordinal) && x.ApplicationId == app));
    }
    [TestMethod]
    [DataRow("after-commit-timeout")] [DataRow("after-commit-malformed")] [DataRow("after-commit-503")]
    public async Task LostResponseAfterCommitPollsAnExistingReceiptWithoutRepeatingTheMutation(string failure)
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerOnboarding: true);
        var app = await App(f); f.Directories.Failure = failure;
        using var created = await Write(f, app); var unknown = await Parse(created, HttpStatusCode.Accepted);
        Assert.AreEqual("unknown", unknown.Status); Assert.IsNull(unknown.Operation); Assert.AreEqual(1, f.Directories.OnboardingCreates);
        f.Directories.Failure = null;
        using var polled = await f.Get(created.Headers.Location!.ToString()); var reconciled = await Parse(polled, HttpStatusCode.Accepted);
        Assert.AreEqual("awaiting-acceptance", reconciled.Status); Assert.AreEqual(1, f.Directories.OnboardingCreates); Assert.AreEqual(1, f.Directories.OnboardingDeliveries);
        f.Directories.CompleteOnboarding = true;
        using var accepted = await f.Get("/api/management/applications/" + app + "/operations/" + reconciled.Operation!.OperationId + "?authority=" + ManagementCustomerIdentityOperations.OnboardingRead);
        var text = await accepted.Content.ReadAsStringAsync(); Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode, text);
        Assert.DoesNotContain(CustomerDirectoryBackend.Canary, text); Assert.Contains("\"completed\":true", text);
    }
    [TestMethod]
    public async Task DeliveryRecoveryRequiresCurrentRetryAndUnderlyingWriteGrants()
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerOnboarding: true);
        var app = await App(f); f.Directories.OnboardingDelivery = "unknown";
        using var created = await Write(f, app); var first = await Parse(created, HttpStatusCode.Accepted);
        using var retry = await Write(f, app, retry: first.Operation!.OperationId, key: "portal-retry-intent");
        var retried = await Parse(retry, HttpStatusCode.Accepted); Assert.AreEqual("awaiting-acceptance", retried.Status);
        using var repeated = await Write(f, app, retry: first.Operation.OperationId, key: "portal-retry-intent"); await Parse(repeated, HttpStatusCode.Accepted);
        Assert.AreEqual(1, f.Directories.OnboardingCreates); Assert.AreEqual(2, f.Directories.OnboardingDeliveries);
        var identity = (f.Data.Access.Org, app, f.Data.Access.Reader); var row = f.Data.Access.Access.Rows[identity];
        f.Data.Access.Access.Rows[identity] = row with { Permissions = row.Permissions.Where(x => x != ManagementCustomerIdentityOperations.RolesAssign).ToArray() };
        using var denied = await Write(f, app, retry: first.Operation.OperationId, key: "portal-retry-intent-2"); Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.AreEqual(2, f.Directories.OnboardingDeliveries);
    }
    [TestMethod]
    public async Task RevokedAssignmentAfterCommitSuppressesOutputAndSubsequentPolling()
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerOnboarding: true); var app = await App(f);
        f.Directories.DuringMutation = _ => { f.Data.Access.Access.Rows.Remove((f.Data.Access.Org, app, f.Data.Access.Reader)); return Task.CompletedTask; };
        using var denied = await Write(f, app); Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode); Assert.AreEqual(1, f.Directories.OnboardingCreates);
        using var read = await f.Get("/api/management/applications/" + app + "/operations/" + f.Directories.Operation + "?authority=" + ManagementCustomerIdentityOperations.OnboardingRead);
        Assert.AreEqual(HttpStatusCode.Forbidden, read.StatusCode); Assert.AreEqual(1, f.Directories.OnboardingDeliveries);
    }
    [TestMethod]
    public async Task ReadOnlyAdminCustomerAndMalformedRequestsNeverObtainMutationAuthority()
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerOnboarding: true);
        var app = await App(f, grants: [ManagementCustomerIdentityOperations.OnboardingRead]);
        foreach (var token in new[] { "reader", "admin", "other", "foreign", "machine" }) {
            using var denied = await Write(f, app, token: token); Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        using var customer = await Write(f, app, token: "customer"); Assert.AreEqual(HttpStatusCode.Unauthorized, customer.StatusCode);
        Assert.AreEqual(0, f.Data.Secrets.Reads); Assert.AreEqual(0, f.Directories.OnboardingCreates);
        var writable = await App(f, "bindry");
        using var malformed = await Write(f, writable, extra: true); Assert.AreEqual(HttpStatusCode.BadRequest, malformed.StatusCode);
        using var empty = await Write(f, writable, name: ""); Assert.AreEqual(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.AreEqual(0, f.Directories.OnboardingCreates);
    }
    [TestMethod]
    public async Task UnknownErrorCodesAreRejectedAndNeverReflectedIntoPortalOrAudit()
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerOnboarding: true, audit: true); var app = await App(f);
        f.Directories.Failure = "onboard-bad-error";
        using var response = await Write(f, app); var result = await Parse(response, HttpStatusCode.Accepted);
        Assert.AreEqual("unknown", result.Status); Assert.IsNull(result.Operation); Assert.DoesNotContain(CustomerDirectoryBackend.Canary, JsonSerializer.Serialize(f.Audit.Rows));
    }
}
