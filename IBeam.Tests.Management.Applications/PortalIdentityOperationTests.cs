using System.Net;
using System.Text.Json;
using IBeam.Management.CustomerIdentity;
namespace IBeam.Tests.Management.Applications;

[TestClass]
public sealed class PortalIdentityOperationTests
{
    [TestMethod]
    public async Task ReceiptMirrorSignsTheSelectedAppAndOriginalWriteAuthorityAndStripsInternalFields()
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerOperations: true, audit: true);
        foreach (var name in new[] { "qurvia", "bindry" }) {
            var app = await f.Directories.AddApp(f, name, [ManagementCustomerIdentityOperations.TenantsCreate], includeOperations: true);
            var path = "/api/management/applications/" + app + "/operations/" + f.Directories.Operation + "?authority=" + ManagementCustomerIdentityOperations.TenantsCreate;
            using var response = await f.Get(path);
            var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
            Assert.DoesNotContain(CustomerDirectoryBackend.Canary, text); Assert.DoesNotContain("fingerprint", text);
            Assert.AreEqual("no-store", response.Headers.CacheControl!.ToString());
            using var json = JsonDocument.Parse(text); Assert.AreEqual(f.Directories.Operation, json.RootElement.GetProperty("data").GetProperty("operationId").GetGuid());
            foreach (var token in new[] { "other", "foreign", "admin", "machine" }) {
                using var denied = await f.Get(path, token); Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode); }
            using var customer = await f.Get(path, "customer"); Assert.AreEqual(HttpStatusCode.Unauthorized, customer.StatusCode);
        }
        Assert.AreEqual(2, f.Audit.Rows.Count(x => x.Operation == "management.portal.customer-identity.operations.read" && x.Succeeded));
    }
    [TestMethod]
    [DataRow("receipt-id")] [DataRow("receipt-authority")]
    public async Task ReceiptSubstitutionFromATargetFailsClosed(string failure)
    {
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerOperations: true);
        var app = await f.Directories.AddApp(f, human: [ManagementCustomerIdentityOperations.TenantsCreate], includeOperations: true);
        f.Directories.Failure = failure;
        using var response = await f.Get("/api/management/applications/" + app + "/operations/" + f.Directories.Operation + "?authority=" + ManagementCustomerIdentityOperations.TenantsCreate);
        Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);
    }
    [TestMethod]
    public async Task ReceiptMirrorsAreExplicitAndUnassignedWriteOrDiscoveryAuthorityCannotPoll()
    {
        await using var off = await PortalHttpFixture.CreateAsync(install: false);
        using var missing = await off.Get("/api/management/applications/" + Guid.NewGuid() + "/operations/" + Guid.NewGuid() + "?authority=" + ManagementCustomerIdentityOperations.TenantsCreate);
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
        await using var f = await PortalHttpFixture.CreateAsync(install: false, customerOperations: true);
        var app = await f.Directories.AddApp(f, includeOperations: true);
        var path = "/api/management/applications/" + app + "/operations/" + f.Directories.Operation;
        using var unassigned = await f.Get(path + "?authority=" + ManagementCustomerIdentityOperations.TenantsCreate);
        Assert.AreEqual(HttpStatusCode.Forbidden, unassigned.StatusCode);
        using var discovery = await f.Get(path + "?authority=" + ManagementCustomerIdentityOperations.Info);
        Assert.AreEqual(HttpStatusCode.BadRequest, discovery.StatusCode);
        Assert.AreEqual(0, f.Data.Secrets.Reads); Assert.AreEqual(0, f.Directories.Calls);
    }
}
