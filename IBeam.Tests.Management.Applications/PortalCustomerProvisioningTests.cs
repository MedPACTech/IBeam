using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IBeam.Management.CustomerIdentity;
namespace IBeam.Tests.Management.Applications;

[TestClass]
public sealed class PortalCustomerProvisioningTests
{
    private static readonly string[] Grants = ManagementCustomerIdentityPermissionPolicy.RequiredFor(ManagementCustomerIdentityOperations.UsersProvision)
        .Concat(new[] { ManagementCustomerIdentityOperations.RolesRead, ManagementCustomerIdentityOperations.InvitesRead }).Distinct().ToArray();
    private static string Path(PortalHttpFixture f, Guid app) => $"/api/management/applications/{app}/tenants/{f.Directories.Tenant}";
    private static Task<Guid> App(PortalHttpFixture f, string name = "qurvia", string[]? grants = null) => f.Directories.AddApp(f, name, grants ?? Grants, includeProvisioning: true);
    private static async Task<HttpResponseMessage> Write(PortalHttpFixture f, Guid app, string action = "users/provision", string key = "portal-customer-intent", string? version = null, object? payload = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Path(f, app) + "/" + action);
        request.Headers.Add("Authorization", "Bearer reader"); request.Headers.Add("Idempotency-Key", key);
        if (version is not null) request.Headers.TryAddWithoutValidation("If-Match", version);
        request.Content = JsonContent.Create(payload ?? (action == "users/provision" ? (object)new ProvisionManagementCustomerUser {
            Email = "customer@example.test", PolicyKey = "default", RoleIds = [f.Directories.ApprovedRole] } : new { }));
        return await f.Client.SendAsync(request);
    }
    private static async Task<T> Read<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected, response.StatusCode, text); Assert.DoesNotContain(CustomerDirectoryBackend.Canary, text);
        using var document = JsonDocument.Parse(text); return document.RootElement.GetProperty("data").Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
    [TestMethod]
    public async Task TwoAppsExposeOnlySelectedCatalogAndInvitationAndKeepProtectedTrackingSeparate()
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerProvisioning: true, audit: true);
        var qurvia = await App(f); var bindry = await App(f, "bindry");
        using var catalogResponse = await f.Get(Path(f, qurvia) + "/assignable-roles");
        var catalog = await Read<ManagementAssignableRoleDto[]>(catalogResponse, HttpStatusCode.OK); Assert.HasCount(1, catalog); Assert.AreEqual("default", catalog[0].PolicyKeys.Single());
        var qurviaInput = new ProvisionManagementCustomerUser { Email = "qurvia@example.test", PolicyKey = "default", RoleIds = [f.Directories.ApprovedRole] };
        var bindryInput = new ProvisionManagementCustomerUser { Email = "bindry@example.test", PolicyKey = "default", RoleIds = [f.Directories.ApprovedRole] };
        using var firstResponse = await Write(f, qurvia, payload: qurviaInput); var first = await Read<ManagementPortalCustomerInvitationResult>(firstResponse, HttpStatusCode.Accepted);
        using var secondResponse = await Write(f, bindry, payload: bindryInput); var second = await Read<ManagementPortalCustomerInvitationResult>(secondResponse, HttpStatusCode.Accepted);
        Assert.AreEqual("qurvia@example.test", first.Invitation!.Email); Assert.AreEqual("bindry@example.test", second.Invitation!.Email);
        Assert.AreEqual(first.Invitation.InviteId, second.Invitation.InviteId); Assert.AreNotEqual(first.Tracking.Reference, second.Tracking.Reference);
        Assert.AreEqual("sent", first.Status); Assert.AreEqual("sent", first.Tracking.Status); Assert.AreEqual(2, f.Directories.InvitationSends);
        using var repeated = await Write(f, qurvia, payload: qurviaInput); await Read<ManagementPortalCustomerInvitationResult>(repeated, HttpStatusCode.Accepted); Assert.AreEqual(2, f.Directories.ProvisionWrites);
        using var cross = await f.Get(firstResponse.Headers.Location!.ToString().Replace(qurvia.ToString(), bindry.ToString())); Assert.AreEqual(HttpStatusCode.BadRequest, cross.StatusCode);
        Assert.DoesNotContain("customer@example.test", JsonSerializer.Serialize(f.Audit.Rows)); Assert.DoesNotContain("qurvia@example.test", JsonSerializer.Serialize(f.Audit.Rows));
        Assert.IsTrue(f.Audit.Rows.Any(x => x.Operation.EndsWith("users.provision.sent", StringComparison.Ordinal) && x.ApplicationId == qurvia));
    }
    [TestMethod]
    public async Task ReadOnlyCatalogAndProvisionOnlyGrantsRemainIndependentAndDoNotImplicitlyAllowRoleChanges()
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerProvisioning: true);
        var reader = await App(f, grants: [ManagementCustomerIdentityOperations.InvitesRead, ManagementCustomerIdentityOperations.RolesRead]);
        using var read = await f.Get(Path(f, reader) + "/invites/" + f.Directories.Invite); await Read<ManagementCustomerInvitationDto>(read, HttpStatusCode.OK);
        using var denied = await Write(f, reader); Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode); Assert.AreEqual(0, f.Directories.InvitationSends);
        var provisioner = await App(f, "bindry", ManagementCustomerIdentityPermissionPolicy.RequiredFor(ManagementCustomerIdentityOperations.UsersProvision).ToArray());
        using var hidden = await f.Get(Path(f, provisioner) + "/assignable-roles"); Assert.AreEqual(HttpStatusCode.Forbidden, hidden.StatusCode);
        using var allowed = await Write(f, provisioner); await Read<ManagementPortalCustomerInvitationResult>(allowed, HttpStatusCode.Accepted);
        using var hiddenInvite = await f.Get(Path(f, provisioner) + "/invites/" + f.Directories.Invite); Assert.AreEqual(HttpStatusCode.Forbidden, hiddenInvite.StatusCode);
        using var poll = await f.Get(allowed.Headers.Location!.ToString()); var receipt = await Read<ManagementPortalOperationResult>(poll, HttpStatusCode.Accepted);
        Assert.AreEqual("sent", receipt.Status); Assert.IsNotNull(receipt.Operation!.Invitation);
    }
    [TestMethod]
    [DataRow("after-commit-timeout")] [DataRow("after-commit-malformed")] [DataRow("after-commit-503")]
    public async Task UnknownProvisioningPollsWithoutSendingAgainAndTracksVerifiedAcceptance(string failure)
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerProvisioning: true);
        var app = await App(f); f.Directories.Failure = failure;
        using var response = await Write(f, app); var unknown = await Read<ManagementPortalCustomerInvitationResult>(response, HttpStatusCode.Accepted);
        Assert.AreEqual("unknown", unknown.Status); Assert.IsNull(unknown.Tracking.Operation); Assert.AreEqual(1, f.Directories.InvitationSends);
        f.Directories.Failure = null;
        using var poll = await f.Get(response.Headers.Location!.ToString()); var sent = await Read<ManagementPortalOperationResult>(poll, HttpStatusCode.Accepted);
        Assert.AreEqual("sent", sent.Status); Assert.AreEqual(1, f.Directories.LookupCalls); Assert.AreEqual(1, f.Directories.InvitationSends);
        f.Directories.CustomerInvitationStatus = "redeemed";
        using var accepted = await f.Get(poll.Headers.Location?.ToString() ?? response.Headers.Location.ToString());
        var completed = await Read<ManagementPortalOperationResult>(accepted, HttpStatusCode.OK); Assert.AreEqual("accepted", completed.Status);
        Assert.AreEqual(1, f.Directories.InvitationSends);
    }
    [TestMethod]
    [DataRow("failed")] [DataRow("unknown")]
    public async Task DeliveryFailuresAreExplicitAndRepeatedRequestDoesNotSendAgain(string status)
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerProvisioning: true); var app = await App(f);
        f.Directories.CustomerDeliveryStatus = status;
        using var response = await Write(f, app); var result = await Read<ManagementPortalCustomerInvitationResult>(response, HttpStatusCode.Accepted); Assert.AreEqual(status, result.Status);
        using var repeated = await Write(f, app); await Read<ManagementPortalCustomerInvitationResult>(repeated, HttpStatusCode.Accepted); Assert.AreEqual(1, f.Directories.InvitationSends);
    }
    [TestMethod]
    public async Task ResendAndRevokeForwardExactVersionsAndRejectStrictBodyOverrides()
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerProvisioning: true); var app = await App(f);
        var action = "invites/" + f.Directories.Invite;
        using var resent = await Write(f, app, action + "/resend", version: "invite-v1");
        var result = await Read<ManagementPortalCustomerInvitationResult>(resent, HttpStatusCode.OK); Assert.AreEqual("sent", result.Status);
        using var replayed = await Write(f, app, action + "/resend", version: "invite-v1"); await Read<ManagementPortalCustomerInvitationResult>(replayed, HttpStatusCode.OK); Assert.AreEqual(1, f.Directories.InvitationSends);
        using var stale = await Write(f, app, action + "/revoke", key: "new-revoke-intent", version: "invite-v1"); Assert.AreEqual(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        using var invalid = await Write(f, app, action + "/revoke", key: "bad-revoke-intent", version: result.Invitation!.Version, payload: new { email = "replacement@example.test" });
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var revoked = await Write(f, app, action + "/revoke", key: "valid-revoke-intent", version: result.Invitation.Version);
        var final = await Read<ManagementPortalCustomerInvitationResult>(revoked, HttpStatusCode.OK); Assert.AreEqual("revoked", final.Status); Assert.AreEqual(1, f.Directories.InvitationSends);
    }
    [TestMethod]
    [DataRow("expired")] [DataRow("revoked")] [DataRow("redeemed")]
    public async Task ReadOnlyInvitationStateAndReceiptUseSafeTerminalStates(string state)
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerProvisioning: true); var app = await App(f);
        using var response = await Write(f, app); await Read<ManagementPortalCustomerInvitationResult>(response, HttpStatusCode.Accepted);
        f.Directories.CustomerInvitationStatus = state;
        using var detail = await f.Get(Path(f, app) + "/invites/" + f.Directories.Invite); var invitation = await Read<ManagementCustomerInvitationDto>(detail, HttpStatusCode.OK); Assert.AreEqual(state, invitation.Status);
        using var poll = await f.Get(response.Headers.Location!.ToString()); var result = await Read<ManagementPortalOperationResult>(poll, HttpStatusCode.OK);
        Assert.AreEqual(state == "redeemed" ? "accepted" : state, result.Status); Assert.AreEqual(1, f.Directories.InvitationSends);
    }
    [TestMethod]
    public async Task PermissionLossAfterRoleDiscoveryAndDuringWriteNeverReturnsCustomerData()
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerProvisioning: true); var app = await App(f);
        using var roles = await f.Get(Path(f, app) + "/assignable-roles"); await Read<ManagementAssignableRoleDto[]>(roles, HttpStatusCode.OK);
        var identity = (f.Data.Access.Org, app, f.Data.Access.Reader); var original = f.Data.Access.Access.Rows[identity];
        f.Data.Access.Access.Rows[identity] = original with { Permissions = [ManagementCustomerIdentityOperations.RolesRead] };
        using var denied = await Write(f, app); Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode); Assert.AreEqual(0, f.Directories.ProvisionWrites);
        f.Data.Access.Access.Rows[identity] = original;
        f.Directories.DuringMutation = _ => { f.Data.Access.Access.Rows[identity] = original with { Permissions = [] }; return Task.CompletedTask; };
        using var hidden = await Write(f, app); Assert.AreEqual(HttpStatusCode.Forbidden, hidden.StatusCode);
        Assert.DoesNotContain("qurvia@example.test", await hidden.Content.ReadAsStringAsync()); Assert.AreEqual(1, f.Directories.ProvisionWrites);
    }
    [TestMethod]
    public async Task TargetRejectsUnapprovedRoleAndPolicyAndPortalRejectsGlobalProfileOrRedirectInput()
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerProvisioning: true); var app = await App(f);
        using var role = await Write(f, app, payload: new { email = "customer@example.test", policyKey = "default", roleIds = new[] { Guid.NewGuid() } });
        Assert.AreEqual(HttpStatusCode.Forbidden, role.StatusCode);
        using var profile = await Write(f, app, payload: new { email = "customer@example.test", policyKey = "default", roleIds = new[] { f.Directories.ApprovedRole }, redirectUrl = CustomerDirectoryBackend.Canary });
        Assert.AreEqual(HttpStatusCode.BadRequest, profile.StatusCode); Assert.DoesNotContain(CustomerDirectoryBackend.Canary, await profile.Content.ReadAsStringAsync());
        Assert.AreEqual(0, f.Directories.ProvisionWrites);
    }
    [TestMethod]
    [DataRow("customer-wrong-tenant")] [DataRow("customer-private-status")]
    public async Task InvalidUpstreamInvitationProjectionsRemainUnknownAndCannotLeakFromMutationOrPoll(string failure)
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerProvisioning: true); var app = await App(f);
        f.Directories.Failure = failure;
        using var response = await Write(f, app); var result = await Read<ManagementPortalCustomerInvitationResult>(response, HttpStatusCode.Accepted);
        Assert.AreEqual("unknown", result.Status); Assert.IsNull(result.Invitation); Assert.IsNull(result.Tracking.Operation);
        using var poll = await f.Get(response.Headers.Location!.ToString()); var status = await Read<ManagementPortalOperationResult>(poll, HttpStatusCode.Accepted);
        Assert.IsNull(status.Operation); Assert.AreEqual("unknown", status.Status); Assert.AreEqual(1, f.Directories.InvitationSends);
    }
    [TestMethod]
    [DataRow("customer-wrong-recipient")] [DataRow("customer-wrong-roles")]
    public async Task UnexpectedUpstreamRecipientOrRolesAreNotReturnedAndMetadataPollingRemainsSafe(string failure)
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerProvisioning: true); var app = await App(f);
        f.Directories.Failure = failure;
        using var response = await Write(f, app); var result = await Read<ManagementPortalCustomerInvitationResult>(response, HttpStatusCode.Accepted);
        Assert.IsNull(result.Invitation); Assert.AreEqual("unknown", result.Status); Assert.DoesNotContain("unrequested@example.test", await response.Content.ReadAsStringAsync());
        using var poll = await f.Get(response.Headers.Location!.ToString()); var status = await Read<ManagementPortalOperationResult>(poll, HttpStatusCode.Accepted);
        Assert.AreEqual("sent", status.Status); Assert.DoesNotContain("unrequested@example.test", await poll.Content.ReadAsStringAsync()); Assert.AreEqual(1, f.Directories.InvitationSends);
    }
}
