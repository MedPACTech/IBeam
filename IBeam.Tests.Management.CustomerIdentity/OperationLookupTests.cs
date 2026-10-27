using System.Net;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class OperationLookupTests
{
    [TestMethod]
    public async Task ScopedLookupReconstructsTheReservedIdentityWithoutTheOriginalPayloadOrAnotherWrite()
    {
        var f = new OperationFixture(); var handler = new OperationHandler(); var request = f.Request();
        var done = await f.Service.ExecuteAsync(request, handler);
        var lookup = (IManagementIdentityOperationLookupService)f.Service;
        Assert.AreEqual(done, await lookup.ResolveAsync(new(request.Operation, request.ResourceScope, request.ExistingResourceId), request.IdempotencyKey, request.Operation));
        Assert.AreEqual(1, handler.Calls);
        await Assert.ThrowsAsync<ManagementCustomerIdentityNotFoundException>(() => lookup.ResolveAsync(new(request.Operation, Guid.NewGuid(), null), request.IdempotencyKey, request.Operation));
        await Assert.ThrowsAsync<ManagementCustomerIdentityNotFoundException>(() => lookup.ResolveAsync(new(request.Operation, request.ResourceScope, null), "different-intent-key", request.Operation));
        f.Actor = f.Actor with { OrganizationId = Guid.NewGuid() };
        await Assert.ThrowsAsync<ManagementCustomerIdentityNotFoundException>(() => lookup.ResolveAsync(new(request.Operation, request.ResourceScope, null), request.IdempotencyKey, request.Operation));
    }
    [TestMethod]
    public async Task SignedLookupIsReadOnlyAndRequiresFreshProofAndOriginalAuthority()
    {
        var unit = new OperationFixture();
        await using var f = await DelegationFixture.Start(operations: unit.Store, operationAudit: unit);
        unit.Options.FingerprintKey = f.App.Services.GetRequiredService<IOptions<ManagementIdentityOperationOptions>>().Value.FingerprintKey;
        unit.Actor = unit.Actor with { OrganizationId = f.Organization, UserId = f.User, ApplicationId = f.Application,
            IdentityRealmId = DiscoveryProtocolTests.Info.IdentityRealmId, CredentialId = f.Credentials.Row.CredentialId };
        var handler = new OperationHandler(); var done = await unit.Service.ExecuteAsync(unit.Request(), handler);
        var original = unit.Store.Rows.Values.Single();
        var body = JsonSerializer.SerializeToUtf8Bytes(new ManagementIdentityOperationLookup(ManagementCustomerIdentityOperations.TenantsCreate, Guid.Empty, null),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        const string path = "/api/management-identity/operations/resolve";
        const string key = "request-12345678";
        var assertion = await f.Issue(ManagementCustomerIdentityOperations.TenantsCreate, "POST", path, body, key);
        using var found = await f.Send(assertion, "POST", path, body, key);
        var text = await found.Content.ReadAsStringAsync(); Assert.AreEqual(HttpStatusCode.OK, found.StatusCode, text);
        using var json = JsonDocument.Parse(text); Assert.AreEqual(done.OperationId, json.RootElement.GetProperty("data").GetProperty("operationId").GetGuid());
        Assert.DoesNotContain(key, text); Assert.DoesNotContain("secret-customer", text);
        Assert.AreEqual(original.Version, unit.Store.Rows.Values.Single().Version); Assert.AreEqual(1, handler.Calls);
        using var replay = await f.Send(assertion, "POST", path, body, key); Assert.AreEqual(HttpStatusCode.Unauthorized, replay.StatusCode);
        using var fresh = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.TenantsCreate, "POST", path, body, key), "POST", path, body, key);
        Assert.AreEqual(HttpStatusCode.OK, fresh.StatusCode);
        using var readOnly = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.TenantsRead, "POST", path, body, key), "POST", path, body, key);
        Assert.AreEqual(HttpStatusCode.Unauthorized, readOnly.StatusCode);
        using var info = await f.Send(null, path: "/api/management-identity/info");
        using var metadata = JsonDocument.Parse(await info.Content.ReadAsStringAsync());
        Assert.AreEqual(1, metadata.RootElement.GetProperty("data").GetProperty("operationReceiptProtocolVersion").GetInt32());
    }
}
