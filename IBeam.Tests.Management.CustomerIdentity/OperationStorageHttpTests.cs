using System.Net;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services.Operations;
using IBeam.Management.Repositories.AzureTable;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class OperationStorageHttpTests
{
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task RealStorageRecoversOneConditionalWriteAndSignsScopedReceiptReadsWithoutLeakingPayloads()
    {
        var connection = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_STORAGE");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("Private Azurite required.");
        var suffix = Guid.NewGuid().ToString("N");
        var table = new TableClient(connection, "Operations" + suffix); var auditTable = new TableClient(connection, "OperationAudit" + suffix);
        var customerTable = new TableClient(connection, "OperationCustomer" + suffix);
        try {
            await table.CreateIfNotExistsAsync(); await auditTable.CreateIfNotExistsAsync(); await customerTable.CreateIfNotExistsAsync();
            var repository = new AzureTableManagementIdentityOperationRepository(table);
            var audit = new AzureTableManagementIdentityOperationAuditRepository(auditTable);
            await using var f = await DelegationFixture.Start(operations: repository, operationAudit: audit);
            var unit = new OperationFixture { Actor = new(f.Organization, f.User, f.Application, DiscoveryProtocolTests.Info.IdentityRealmId,
                f.Credentials.Row.CredentialId, "atlas", "Testing", ManagementCustomerIdentityOperations.TenantsCreate,
                Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(1), new string('a',32)) };
            unit.Options.FingerprintKey = f.App.Services.GetRequiredService<IOptions<ManagementIdentityOperationOptions>>().Value.FingerprintKey;
            IManagementIdentityOperationService Restart() => new ManagementIdentityOperationService(repository, audit, unit,
                Options.Create(unit.Options), unit.Clock, new ServiceOperationExecutor());
            var handler = new TableHandler(customerTable) { FailAfterWrite = true };
            await Assert.ThrowsAsync<ManagementCustomerIdentityUnavailableException>(() => Restart().ExecuteAsync(unit.Request(), handler));
            handler.FailAfterWrite = false;
            var done = await Restart().ExecuteAsync(unit.Request(), handler);
            Assert.AreEqual("succeeded", done.Status); Assert.AreEqual(1, handler.Writes);
            var scope = new ManagementIdentityOperationScope(unit.Actor.IdentityRealmId, f.Organization, f.Application);
            var row = (await repository.GetAsync(scope, done.OperationId))!;
            await Assert.ThrowsAsync<ManagementIdentityOperationConflictException>(() => repository.ReplaceAsync(row, "*"));
            var stale = row.Version; row = await repository.ReplaceAsync(row, row.Version);
            await Assert.ThrowsAsync<ManagementIdentityOperationConflictException>(() => repository.ReplaceAsync(row, stale));
            Assert.IsNull(await repository.GetAsync(scope with { ApplicationId = Guid.NewGuid() }, done.OperationId));
            var path = "/api/management-identity/operations/" + done.OperationId;
            var assertion = await f.Issue(ManagementCustomerIdentityOperations.TenantsCreate, path: path);
            for (var repeat = 0; repeat < 2; repeat++) {
                using var response = await f.Send(assertion, path: path);
                var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
                Assert.DoesNotContain("Fingerprint", text); Assert.DoesNotContain("request-12345678", text); Assert.DoesNotContain("secret-customer", text);
                using var json = JsonDocument.Parse(text); Assert.AreEqual(done.OperationId, json.RootElement.GetProperty("data").GetProperty("operationId").GetGuid());
                Assert.AreEqual("no-store", response.Headers.CacheControl!.ToString());
            }
            Assert.HasCount(0, f.Replay.Claims);
            var lookupBody = JsonSerializer.SerializeToUtf8Bytes(new ManagementIdentityOperationLookup(ManagementCustomerIdentityOperations.TenantsCreate, Guid.Empty, null),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            const string lookupPath = "/api/management-identity/operations/resolve";
            var operationVersion = (await repository.GetAsync(scope, done.OperationId))!.Version;
            using var resolved = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.TenantsCreate, "POST", lookupPath, lookupBody, "request-12345678"),
                "POST", lookupPath, lookupBody, "request-12345678");
            Assert.AreEqual(HttpStatusCode.OK, resolved.StatusCode, await resolved.Content.ReadAsStringAsync());
            Assert.AreEqual(operationVersion, (await repository.GetAsync(scope, done.OperationId))!.Version); Assert.AreEqual(1, handler.Writes);
            using var wrong = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.TenantsRead, path: path), path: path);
            Assert.AreEqual(HttpStatusCode.Unauthorized, wrong.StatusCode);
            using var unrelated = await f.Send(await f.Issue(ManagementCustomerIdentityOperations.OnboardingRead, path: path), path: path);
            Assert.AreEqual(HttpStatusCode.Forbidden, unrelated.StatusCode);
            f.Credentials.Row = f.Credentials.Row with { Status = "Revoked", RevokedUtc = DateTimeOffset.UtcNow };
            using var revoked = await f.Send(assertion, path: path); Assert.AreEqual(HttpStatusCode.Unauthorized, revoked.StatusCode);
            await foreach (var stored in table.QueryAsync<TableEntity>()) {
                Assert.DoesNotContain("secret-customer", JsonSerializer.Serialize(stored)); Assert.DoesNotContain("request-12345678", JsonSerializer.Serialize(stored));
            }
            var events = new List<TableEntity>(); await foreach (var stored in auditTable.QueryAsync<TableEntity>()) events.Add(stored);
            Assert.HasCount(3, events); // intent, uncertain lost reply, then success
            Assert.DoesNotContain("secret-customer", JsonSerializer.Serialize(events));
            var eventData = new IBeam.Management.Entities.ManagementAuditEntity { AuditId = Guid.NewGuid(), OrganizationId = f.Organization,
                OccurredUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(1), Operation = "management.identity.tenants.create.intent" };
            await audit.AppendOnceAsync(eventData); await audit.AppendOnceAsync(eventData);
            await Assert.ThrowsAsync<InvalidOperationException>(() => audit.AppendOnceAsync(eventData with { Succeeded = true }));
            var concurrent = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Restart().ExecuteAsync(unit.Request(key: "parallel-12345678"), handler)));
            Assert.AreEqual(1, concurrent.Select(x => x.OperationId).Distinct().Count());
            Assert.AreEqual(1, concurrent.Select(x => x.ResourceId).Distinct().Count());
            Assert.AreEqual(2, handler.Writes); // one earlier recovery, one shared concurrent reservation
        }
        finally { await table.DeleteAsync(); await auditTable.DeleteAsync(); await customerTable.DeleteAsync(); }
    }
    [TestMethod]
    public async Task IdempotencyHeaderIsMandatoryAndSignedBeforeAnyMutationOrReplayClaim()
    {
        await using var f = await DelegationFixture.Start();
        var assertion = await f.Issue(ManagementCustomerIdentityOperations.TenantsCreate, "POST");
        foreach (var header in new[] { "missing", "changed", "duplicate" }) {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/management-identity/probe");
            request.Headers.Add("X-IBeam-Management-Key", f.Key); request.Headers.Add("X-IBeam-Management-Actor", assertion.Value);
            if (header != "missing") request.Headers.Add("Idempotency-Key", header == "duplicate" ? new[] { "fixture-idempotency", "fixture-idempotency" } : new[] { "another-idempotency" });
            using var denied = await f.Client.SendAsync(request); Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode);
        }
        Assert.AreEqual(0, f.Executions); Assert.HasCount(0, f.Replay.Claims);
        using var valid = await f.Send(assertion, "POST"); Assert.AreEqual(HttpStatusCode.OK, valid.StatusCode);
        Assert.AreEqual(1, f.Executions);
    }
    private sealed class TableHandler(TableClient table) : IManagementIdentityMutationHandler
    {
        public bool FailAfterWrite { get; set; }
        public int Writes { get; private set; }
        public async Task<ManagementIdentityReconciliation> ReconcileAsync(ManagementIdentityMutationContext context, CancellationToken ct)
            => (await table.GetEntityIfExistsAsync<TableEntity>("TEN", context.ResourceId.ToString("D"), cancellationToken: ct)).HasValue
                ? ManagementIdentityReconciliation.Applied : ManagementIdentityReconciliation.Absent;
        public async Task ApplyAsync(ManagementIdentityMutationContext context, CancellationToken ct)
        {
            await table.AddEntityAsync(new TableEntity("TEN", context.ResourceId.ToString("D")) { ["Name"] = "secret-customer@example.test" }, ct);
            Writes++; if (FailAfterWrite) throw new Exception("lost-provider-reply");
        }
    }
}
