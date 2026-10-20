using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IBeam.Management.CustomerIdentity;
using IBeam.Management;
using Microsoft.Extensions.DependencyInjection;
namespace IBeam.Tests.Management.Applications;

[TestClass]
public sealed class PortalTenantAdministrationTests
{
    private static readonly string[] Writes = [ManagementCustomerIdentityOperations.TenantsCreate, ManagementCustomerIdentityOperations.TenantsUpdate];
    private static string Path(Guid app, Guid? tenant = null) => "/api/management/applications/" + app + "/tenants" + (tenant is Guid id ? "/" + id : "");
    private static Task<Guid> App(PortalHttpFixture f, string name = "qurvia", string[]? grants = null)
        => f.Directories.AddApp(f, name, grants ?? Writes, includeMutations: true);
    private static async Task<HttpResponseMessage> Write(PortalHttpFixture f, Guid app, string name = "Enterprise Customer", string key = "portal-intent-123456",
        Guid? tenant = null, string? version = null, string token = "reader")
    {
        using var request = new HttpRequestMessage(tenant is null ? HttpMethod.Post : HttpMethod.Patch, Path(app, tenant));
        request.Headers.Add("Authorization", "Bearer " + token); request.Headers.Add("Idempotency-Key", key);
        if (version is not null) request.Headers.TryAddWithoutValidation("If-Match", version);
        request.Content = JsonContent.Create(new { name }); return await f.Client.SendAsync(request);
    }
    private static async Task<JsonDocument> Parse(HttpResponseMessage response, HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected, response.StatusCode, text);
        Assert.DoesNotContain(CustomerDirectoryBackend.Canary, text); Assert.DoesNotContain("portal-intent-123456", text);
        Assert.DoesNotContain(ConnectionFixture.Key, text); Assert.AreEqual("no-store", response.Headers.CacheControl!.ToString());
        return JsonDocument.Parse(text);
    }
    [TestMethod]
    public async Task TwoAppsWithOverlappingTenantIdsKeepWritesAndAuditScopesIsolated()
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerMutations: true, audit: true);
        foreach (var name in new[] { "qurvia", "bindry" }) {
            var app = await App(f, name);
            using var created = await Write(f, app, name + " Customer"); using var json = await Parse(created, HttpStatusCode.Created);
            var data = json.RootElement.GetProperty("data"); var tenant = data.GetProperty("tenant");
            Assert.AreEqual(f.Directories.Tenant, tenant.GetProperty("tenantId").GetGuid()); Assert.AreEqual(name + " Customer", tenant.GetProperty("name").GetString());
            Assert.AreEqual("succeeded", data.GetProperty("tracking").GetProperty("status").GetString()); Assert.IsNotNull(created.Headers.Location);
            using var update = await Write(f, app, name + " Renamed", tenant: f.Directories.Tenant, version: tenant.GetProperty("version").GetString());
            using var updated = await Parse(update, HttpStatusCode.OK);
            Assert.AreEqual(name + " Renamed", updated.RootElement.GetProperty("data").GetProperty("tenant").GetProperty("name").GetString());
            var audit = f.Audit.Rows.Single(x => x.ApplicationId == app && x.Operation == "management.portal.customer-identity.tenants.create.succeeded");
            Assert.AreEqual(f.Directories.Tenant, audit.SubjectId); Assert.AreEqual(f.Data.Access.Reader, audit.ActorUserId);
        }
        Assert.AreEqual(4, f.Directories.MutationWrites); Assert.DoesNotContain("Customer", JsonSerializer.Serialize(f.Audit.Rows));
    }
    [TestMethod]
    public async Task DuplicateSubmissionsKeepOneIdempotencyIdentityAndFreshActorProofs()
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerMutations: true);
        var app = await App(f);
        using var first = await Write(f, app); using var a = await Parse(first, HttpStatusCode.Created);
        using var repeat = await Write(f, app); using var b = await Parse(repeat, HttpStatusCode.Created);
        Assert.AreEqual(a.RootElement.GetProperty("data").GetProperty("tracking").GetProperty("operation").GetProperty("operationId").GetGuid(),
            b.RootElement.GetProperty("data").GetProperty("tracking").GetProperty("operation").GetProperty("operationId").GetGuid());
        Assert.AreEqual(1, f.Directories.MutationWrites); Assert.AreEqual(2, f.Directories.MutationCalls);
        Assert.AreEqual(2, f.Directories.MutationProofs.Distinct().Count());
        using var conflict = await Write(f, app, "Different Payload"); Assert.AreEqual(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.DoesNotContain(CustomerDirectoryBackend.Canary, await conflict.Content.ReadAsStringAsync()); Assert.AreEqual(1, f.Directories.MutationWrites);
    }
    [TestMethod]
    public async Task ConcurrentRenamePreservesTheOriginalVersionAndOnlyOneWriterSucceeds()
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerMutations: true); var app = await App(f);
        using var created = await Write(f, app); using var json = await Parse(created, HttpStatusCode.Created);
        var version = json.RootElement.GetProperty("data").GetProperty("tenant").GetProperty("version").GetString();
        var responses = await Task.WhenAll(Write(f, app, "First Rename", "rename-intent-one", f.Directories.Tenant, version),
            Write(f, app, "Second Rename", "rename-intent-two", f.Directories.Tenant, version));
        try { CollectionAssert.AreEquivalent(new[] { HttpStatusCode.OK, HttpStatusCode.PreconditionFailed }, responses.Select(x => x.StatusCode).ToArray()); }
        finally { foreach (var response in responses) response.Dispose(); }
        Assert.AreEqual(2, f.Directories.MutationWrites);
        using var missing = await Write(f, app, "No version", tenant: f.Directories.Tenant); Assert.AreEqual(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.AreEqual(2, f.Directories.MutationWrites);
    }
    [TestMethod]
    [DataRow("after-commit-timeout")] [DataRow("after-commit-malformed")] [DataRow("after-commit-503")]
    public async Task LostRepliesReturnAnUnknownReferenceAndPollReadOnlyWithoutRepeatingTheWrite(string failure)
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerMutations: true, audit: true); var app = await App(f);
        f.Directories.Failure = failure;
        using var pending = await Write(f, app); using var json = await Parse(pending, HttpStatusCode.Accepted);
        var data = json.RootElement.GetProperty("data"); Assert.AreEqual("unknown", data.GetProperty("tracking").GetProperty("status").GetString());
        Assert.AreEqual(JsonValueKind.Null, data.GetProperty("tenant").ValueKind); Assert.IsNotNull(pending.Headers.Location);
        Assert.AreEqual(1, f.Directories.MutationWrites); Assert.AreEqual(1, f.Directories.MutationCalls);
        var reference = data.GetProperty("tracking").GetProperty("reference").GetString()!;
        Assert.DoesNotContain("portal-intent-123456", reference); Assert.DoesNotContain("Enterprise", reference);
        f.Directories.Failure = null;
        using var poll = await f.Get(pending.Headers.Location.ToString()); using var found = await Parse(poll, HttpStatusCode.OK);
        Assert.AreEqual("succeeded", found.RootElement.GetProperty("data").GetProperty("status").GetString());
        Assert.AreEqual(data.GetProperty("tracking").GetProperty("expiresUtc").GetDateTimeOffset(), found.RootElement.GetProperty("data").GetProperty("expiresUtc").GetDateTimeOffset());
        Assert.AreEqual(f.Directories.Tenant, found.RootElement.GetProperty("data").GetProperty("operation").GetProperty("resourceId").GetGuid());
        Assert.AreEqual(1, f.Directories.MutationCalls); Assert.AreEqual(1, f.Directories.LookupCalls); Assert.AreEqual(1, f.Directories.MutationWrites);
        Assert.IsNull(f.Audit.Rows.Single(x => x.Operation.EndsWith("create.unknown", StringComparison.Ordinal)).SubjectId);
        Assert.AreEqual(f.Directories.Tenant, f.Audit.Rows.Single(x => x.Operation.EndsWith("reconcile.succeeded", StringComparison.Ordinal)).SubjectId);
        Assert.DoesNotContain("Enterprise", JsonSerializer.Serialize(f.Audit.Rows));
    }
    [TestMethod]
    public async Task RevocationBeforeRetryOrAfterCommitPreventsFurtherReadsAndWrites()
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerMutations: true); var app = await App(f);
        f.Directories.Failure = "after-commit-503";
        using var pending = await Write(f, app); using var json = await Parse(pending, HttpStatusCode.Accepted);
        var key = (f.Data.Access.Org, app, f.Data.Access.Reader); var grant = f.Data.Access.Access.Rows[key];
        f.Data.Access.Access.Rows[key] = grant with { RevokedUtc = DateTimeOffset.UtcNow }; f.Directories.Failure = null;
        using var retry = await Write(f, app); Assert.AreEqual(HttpStatusCode.Forbidden, retry.StatusCode);
        using var poll = await f.Get(pending.Headers.Location!.ToString()); Assert.AreEqual(HttpStatusCode.Forbidden, poll.StatusCode);
        Assert.AreEqual(1, f.Directories.MutationCalls); Assert.AreEqual(0, f.Directories.LookupCalls);
        f.Data.Access.Access.Rows[key] = grant;
        f.Directories.DuringMutation = _ => { f.Data.Access.Access.Rows[key] = grant with { RevokedUtc = DateTimeOffset.UtcNow }; return Task.CompletedTask; };
        using var changed = await Write(f, app, key: "new-intent-123456"); Assert.AreEqual(HttpStatusCode.Forbidden, changed.StatusCode);
        Assert.DoesNotContain("Enterprise", await changed.Content.ReadAsStringAsync());
    }
    [TestMethod]
    public async Task PendingReferencesRejectUserAppAuthorityConnectionAndExpiryReplay()
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerMutations: true); var app = await App(f); var otherApp = await App(f, "bindry");
        f.Directories.Failure = "after-commit-503";
        using var pending = await Write(f, app); using var json = await Parse(pending, HttpStatusCode.Accepted);
        var reference = json.RootElement.GetProperty("data").GetProperty("tracking").GetProperty("reference").GetString();
        string Location(Guid id, string authority) => "/api/management/applications/" + id + "/operations/" + reference + "?authority=" + authority;
        var original = f.Data.Access.Access.Rows[(f.Data.Access.Org, app, f.Data.Access.Reader)];
        f.Data.Access.Access.Rows[(f.Data.Access.Org, app, f.OtherUser)] = original with { UserId = f.OtherUser };
        using var user = await f.Get(Location(app, Writes[0]), "other"); Assert.AreEqual(HttpStatusCode.BadRequest, user.StatusCode);
        using var other = await f.Get(Location(otherApp, Writes[0])); Assert.AreEqual(HttpStatusCode.BadRequest, other.StatusCode);
        using var authority = await f.Get(Location(app, Writes[1])); Assert.AreEqual(HttpStatusCode.BadRequest, authority.StatusCode);
        var credentialIndex = f.Data.Credentials.Rows.FindIndex(x => x.ApplicationId == app); var credential = f.Data.Credentials.Rows[credentialIndex];
        f.Data.Credentials.Rows[credentialIndex] = credential with { CustomerIdentity = credential.CustomerIdentity! with {
            Info = credential.CustomerIdentity.Info! with { IdentityRealmId = Guid.NewGuid() } } };
        using var realm = await f.Get(Location(app, Writes[0])); Assert.AreEqual(HttpStatusCode.BadRequest, realm.StatusCode);
        f.Data.Credentials.Rows[credentialIndex] = credential;
        var appKey = (f.Data.Access.Org, app); var row = f.Data.Access.Apps.Rows[appKey]; f.Data.Access.Apps.Rows[appKey] = row with { Version = "changed" };
        using var connection = await f.Get(Location(app, Writes[0])); Assert.AreEqual(HttpStatusCode.BadRequest, connection.StatusCode);
        f.Data.Access.Apps.Rows[appKey] = row; f.Data.Clock.Now = f.Data.Clock.Now.AddMinutes(16);
        using var expired = await f.Get(Location(app, Writes[0])); Assert.AreEqual(HttpStatusCode.BadRequest, expired.StatusCode);
        Assert.AreEqual(0, f.Directories.LookupCalls);
    }
    [TestMethod]
    public async Task ReadOnlyAndAdministrativePrincipalsCannotMutateOrResolveSecrets()
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerMutations: true);
        var app = await App(f, grants: [ManagementCustomerIdentityOperations.TenantsRead]);
        foreach (var token in new[] { "reader", "admin", "other", "foreign", "machine" }) {
            using var denied = await Write(f, app, token: token); Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode); }
        using var customer = await Write(f, app, token: "customer"); Assert.AreEqual(HttpStatusCode.Unauthorized, customer.StatusCode);
        Assert.AreEqual(0, f.Data.Secrets.Reads); Assert.AreEqual(0, f.Directories.Calls);
    }
    [TestMethod]
    public async Task CachedMissingMachineCapabilityAndInvalidCommandsCannotReachTargetWrites()
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerMutations: true); var app = await App(f);
        using var invalid = await Write(f, app, "bad\nname"); Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.AreEqual(0, f.Data.Secrets.Reads);
        var old = f.Data.Credentials.Rows[0]; f.Data.Credentials.Rows[0] = old with { Capabilities = old.Capabilities.Where(x => x != Writes[0]).ToArray() };
        using var denied = await Write(f, app); Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.AreEqual(0, f.Data.Secrets.Reads); Assert.AreEqual(0, f.Directories.MutationCalls);
    }
    [TestMethod]
    public async Task OldReceiptProtocolsDoNotReceiveWritesAndUnresolvedReferencesNeverClaimFailure()
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerMutations: true); var app = await App(f);
        f.Directories.Failure = "old-receipt-protocol";
        using var pending = await Write(f, app); using var json = await Parse(pending, HttpStatusCode.Accepted);
        Assert.AreEqual("unknown", json.RootElement.GetProperty("data").GetProperty("tracking").GetProperty("status").GetString());
        Assert.AreEqual(0, f.Directories.MutationCalls);
        f.Directories.Failure = null;
        using var absent = await f.Get(pending.Headers.Location!.ToString()); using var status = await Parse(absent, HttpStatusCode.Accepted);
        Assert.AreEqual("unknown", status.RootElement.GetProperty("data").GetProperty("status").GetString());
        Assert.AreEqual(0, f.Directories.MutationWrites); Assert.AreEqual(1, f.Directories.LookupCalls);
    }
    [TestMethod]
    public async Task DirectServiceCallsAndConsumerProfileFieldsCannotBypassThePortalBoundary()
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerMutations: true); var app = await App(f);
        using var scope = f.App.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IManagementPortalTenantAdministrationService>();
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => service.CreateAsync(app, new() { Name = "Enterprise" }, "portal-intent-123456"));
        using var request = new HttpRequestMessage(HttpMethod.Post, Path(app));
        request.Headers.Add("Authorization", "Bearer reader"); request.Headers.Add("Idempotency-Key", "portal-intent-123456");
        request.Content = JsonContent.Create(new { name = "Enterprise", profile = CustomerDirectoryBackend.Canary });
        using var response = await f.Client.SendAsync(request); Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(CustomerDirectoryBackend.Canary, await response.Content.ReadAsStringAsync());
        Assert.AreEqual(0, f.Data.Secrets.Reads); Assert.AreEqual(0, f.Directories.MutationCalls);
    }
}
