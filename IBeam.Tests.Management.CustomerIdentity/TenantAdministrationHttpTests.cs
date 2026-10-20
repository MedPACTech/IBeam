using System.Net;
using System.Text;
using System.Text.Json;
using IBeam.Management.CustomerIdentity;
namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class TenantAdministrationHttpTests
{
    private const string Path = "/api/management-identity/tenants";
    [TestMethod]
    public async Task SignedCreateAndVersionedPatchReturnSafeEnvelopesAndOperationLocation()
    {
        var store = new ConditionalTenantStore(); var metadata = new TenantMetadataStore();
        await using var f = await DelegationFixture.Start(tenantStore: store, tenantMetadata: metadata);
        var body = Encoding.UTF8.GetBytes("{\"name\":\"Enterprise\",\"externalReference\":\"CRM:123\"}");
        using var response = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.TenantsCreate, "POST", Path, body), "POST", Path, body);
        var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, text);
        using var created = JsonDocument.Parse(text); var data = created.RootElement.GetProperty("data");
        var tenant = data.GetProperty("tenant").GetProperty("tenantId").GetGuid(); var version = data.GetProperty("tenant").GetProperty("version").GetString()!;
        Assert.AreEqual("support-provisioned", data.GetProperty("source").GetString()); Assert.IsNotNull(response.Headers.Location);
        var operationPath = "/api/management-identity/operations/" + data.GetProperty("operation").GetProperty("operationId").GetGuid();
        Assert.IsTrue(response.Headers.Location.ToString().EndsWith(operationPath, StringComparison.Ordinal));
        Assert.AreEqual("no-store", response.Headers.CacheControl!.ToString()); Assert.IsNull(f.TenantHooks.Context!.AuthUserId);
        var rename = Encoding.UTF8.GetBytes("{\"name\":\"Renamed\"}"); var path = Path + "/" + tenant;
        var proof = await f.Issue(ManagementCustomerIdentityOperations.TenantsUpdate, "PATCH", path, rename, ifMatch: version);
        using var tampered = await f.Send(proof, "PATCH", path, rename, ifMatch: "another-version");
        Assert.AreEqual(HttpStatusCode.Unauthorized, tampered.StatusCode); Assert.AreEqual(0, store.Renames);
        using var updated = await f.Send(proof, "PATCH", path, rename, ifMatch: version);
        Assert.AreEqual(HttpStatusCode.OK, updated.StatusCode, await updated.Content.ReadAsStringAsync());
        using var retry = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.TenantsUpdate, "PATCH", path, rename, ifMatch: version), "PATCH", path, rename, ifMatch: version);
        Assert.AreEqual(HttpStatusCode.OK, retry.StatusCode); Assert.AreEqual(1, store.Renames);
        using var stale = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.TenantsUpdate, "PATCH", path, rename,
            idempotencyKey: "new-rename-intent", ifMatch: version), "PATCH", path, rename, idempotencyKey: "new-rename-intent", ifMatch: version);
        Assert.AreEqual(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.DoesNotContain("Enterprise", JsonSerializer.Serialize(f.Audit.Rows)); Assert.DoesNotContain("CRM:123", JsonSerializer.Serialize(f.Audit.Rows));
    }
    [TestMethod]
    [DataRow("{\"name\":\"\"}")] [DataRow("{\"name\":\"Enterprise\",\"profile\":{\"secret\":\"PROFILE-CANARY\"}}")]
    [DataRow("{\"name\":\"Enterprise\",\"roles\":[\"Owner\"]}")] [DataRow("{\"name\":\"Enterprise\",\"externalReference\":\"email@example.test\"}")]
    public async Task InvalidAndConsumerProfileFieldsAreRejectedBeforeAnyTenantCreation(string json)
    {
        var store = new ConditionalTenantStore(); await using var f = await DelegationFixture.Start(tenantStore: store, tenantMetadata: new TenantMetadataStore());
        var body = Encoding.UTF8.GetBytes(json);
        using var response = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.TenantsCreate, "POST", Path, body), "POST", Path, body);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode); Assert.AreEqual(0, store.Creates);
        Assert.DoesNotContain("PROFILE-CANARY", await response.Content.ReadAsStringAsync());
    }
    [TestMethod]
    public async Task UnsupportedLegacyAdaptersAdvertiseCreationButNoUpdateCapability()
    {
        var store = new ConditionalTenantStore(); await using var f = await DelegationFixture.Start(tenantStore: new LegacyTenantStore(store), tenantMetadata: new TenantMetadataStore());
        using var response = await f.Send(null, path: "/api/management-identity/info");
        using var info = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var capabilities = info.RootElement.GetProperty("data").GetProperty("capabilities").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Contains(ManagementCustomerIdentityOperations.TenantsCreate, capabilities); Assert.DoesNotContain(ManagementCustomerIdentityOperations.TenantsUpdate, capabilities);
        var body = Encoding.UTF8.GetBytes("{\"name\":\"Changed\"}"); var path = Path + "/" + Guid.NewGuid();
        using var denied = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.TenantsUpdate, "PATCH", path, body, ifMatch: "version"), "PATCH", path, body, ifMatch: "version");
        Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode); Assert.AreEqual(0, store.Renames);
    }
    [TestMethod]
    public async Task ConsumerFailureIsSafeAndARepeatedIntentRecoversWithoutAnotherTenant()
    {
        var store = new ConditionalTenantStore(); await using var f = await DelegationFixture.Start(tenantStore: store, tenantMetadata: new TenantMetadataStore());
        f.TenantHooks.FailCreated = true; var body = Encoding.UTF8.GetBytes("{\"name\":\"Enterprise\"}");
        using var failed = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.TenantsCreate, "POST", Path, body), "POST", Path, body);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, failed.StatusCode); Assert.DoesNotContain("CONSUMER-PROFILE", await failed.Content.ReadAsStringAsync());
        f.TenantHooks.FailCreated = false;
        using var retry = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.TenantsCreate, "POST", Path, body), "POST", Path, body);
        Assert.AreEqual(HttpStatusCode.Created, retry.StatusCode); Assert.AreEqual(1, store.Creates); Assert.HasCount(1, store.Rows);
    }
}
