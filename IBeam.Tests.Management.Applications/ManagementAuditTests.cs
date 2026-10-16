using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using IBeam.Management;
using IBeam.Management.Entities;
using IBeam.Management.Repositories.AzureTable;
using IBeam.Management.Services;
using IBeam.Management.Services.Audit;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
namespace IBeam.Tests.Management.Applications;
internal sealed class MemoryManagementAudits : IManagementAuditRepository
{
    public List<ManagementAuditEntity> Rows { get; } = [];
    public bool Outage { get; set; }
    public int Calls { get; private set; }
    public Task AppendAsync(ManagementAuditEntity row, CancellationToken ct = default)
    { Calls++; if (Outage) throw new Exception(ConnectionFixture.Key); Rows.Add(row); return Task.CompletedTask; }
}
[TestClass]
public sealed class ManagementAuditTests
{
    [TestMethod]
    public async Task HttpReadsAndDenialsPersistMetadataAndIgnoreBrowserActorHeaders()
    {
        await using var f = await PortalHttpFixture.CreateAsync(selector: true, audit: true);
        var app = await f.Data.AddApp();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/management/applications/{app}/system-errors?pageSize=1");
        request.Headers.Add("Authorization", "Bearer reader");
        request.Headers.Add("X-IBeam-Management-Actor", ConnectionFixture.Key);
        request.Headers.Add("X-IBeam-Management-Correlation", new string('f', 32));
        Assert.AreEqual(HttpStatusCode.OK, (await f.Client.SendAsync(request)).StatusCode);
        var read = f.Audit.Rows.Single(x => x.Operation == "management.portal.errors.list");
        Assert.AreEqual(f.Data.Access.Reader, read.ActorUserId); Assert.AreEqual(f.Data.Access.Org, read.OrganizationId); Assert.AreEqual(app, read.ApplicationId);
        Assert.IsTrue(read.Succeeded); Assert.IsNull(read.CredentialId); Assert.AreNotEqual(new string('f', 32), read.CorrelationId);
        Assert.AreEqual(TimeSpan.FromDays(90), read.ExpiresUtc - read.OccurredUtc);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await f.Get($"/api/management/applications/{app}/system-logs?pageSize=1", "other")).StatusCode);
        Assert.IsTrue(f.Audit.Rows.Any(x => x.ActorUserId == f.OtherUser && !x.Succeeded));
        Assert.DoesNotContain(ConnectionFixture.Key, JsonSerializer.Serialize(f.Audit.Rows));
        var count = f.Audit.Rows.Count;
        Assert.AreEqual(HttpStatusCode.NotFound, (await f.Get("/api/management/audit")).StatusCode);
        Assert.AreEqual(count, f.Audit.Rows.Count);
    }
    [TestMethod]
    public async Task GenericCanariesNeverReachSinkAndMachineIdentityUsesVerifiedDeploymentClaims()
    {
        using var f = new AccessFixture(); var rows = new MemoryManagementAudits(); var context = new ManagementAuditContext();
        var settings = new ManagementAuditOptions { RetentionDays = 7 };
        var sink = new ManagementAuditSink(rows, f, context, Options.Create(settings), Options.Create(new ManagementDiagnosticsOptions()), TimeProvider.System);
        foreach (var action in new[] { "management.applications.update", "management.applications.connection.replace", "management.support-invitations.create", "management.application-access.grant", "management.application-access.revoke" })
        foreach (var succeeded in new[] { true, false })
            await sink.WriteTransactionAsync(new() { Action = action, EntityId = Guid.NewGuid(), Succeeded = succeeded, ActorId = ConnectionFixture.Key,
                OriginalJson = ConnectionFixture.Key, TransformedJson = ConnectionFixture.Key, ErrorMessage = ConnectionFixture.Key, ErrorType = ConnectionFixture.Key,
                ServiceName = ConnectionFixture.Key, EntityName = ConnectionFixture.Key, CorrelationId = ConnectionFixture.Key, IpAddress = ConnectionFixture.Key, UserAgent = ConnectionFixture.Key });
        Assert.AreEqual(10, rows.Rows.Count); Assert.DoesNotContain(ConnectionFixture.Key, JsonSerializer.Serialize(rows.Rows));
        Assert.IsTrue(rows.Rows.All(x => x.ActorUserId == f.Admin && x.OrganizationId == f.Org && x.ExpiresUtc - x.OccurredUtc == TimeSpan.FromDays(7)));
        await sink.WriteTransactionAsync(new() { Action = ConnectionFixture.Key }); Assert.AreEqual(10, rows.Rows.Count);
        var id = Guid.NewGuid();
        var principal = new Principal(new(new ClaimsIdentity(new[] { new Claim(ManagementClaimTypes.SubjectType, ManagementSubjectTypes.Credential),
            new Claim(ManagementClaimTypes.CredentialId, id.ToString()), new Claim(ManagementClaimTypes.ApplicationKey, "qurvia"), new Claim(ManagementClaimTypes.Environment, "Production") }, "Machine")));
        context.AcceptMachineCorrelation(new string('a', 32));
        sink = new(rows, principal, context, Options.Create(settings), Options.Create(new ManagementDiagnosticsOptions { ApplicationKey = "qurvia", Environment = "Production" }), TimeProvider.System);
        await sink.WriteTransactionAsync(new() { Action = ManagementOperations.ErrorsRead, Succeeded = true, ActorId = ConnectionFixture.Key });
        var machine = rows.Rows[^1]; Assert.AreEqual(id, machine.CredentialId); Assert.IsNull(machine.ActorUserId); Assert.AreEqual("qurvia", machine.ApplicationKey); Assert.AreEqual(new string('a', 32), machine.CorrelationId);
        context.AcceptMachineCorrelation(ConnectionFixture.Key); Assert.AreEqual(new string('a', 32), context.CorrelationId);
    }
    [TestMethod]
    public async Task FailurePolicyIsBoundedSafeAndDoesNotChangeCustomerExecutorPolicy()
    {
        using var f = new AccessFixture(); var rows = new MemoryManagementAudits { Outage = true }; var options = new ManagementAuditOptions();
        var sink = new ManagementAuditSink(rows, f, new ManagementAuditContext(), Options.Create(options), Options.Create(new ManagementDiagnosticsOptions()), TimeProvider.System);
        await Assert.ThrowsAsync<ManagementAuditUnavailableException>(() => sink.WriteTransactionAsync(new() { Action = ManagementOperations.IdentityRead }));
        options.FailOnWriteError = false; await sink.WriteTransactionAsync(new() { Action = ManagementOperations.IdentityRead });
        options.RetentionDays = 0; Assert.IsFalse(options.IsValid()); options.RetentionDays = 3651; Assert.IsFalse(options.IsValid());
        var services = new ServiceCollection(); services.AddIBeamManagementServices();
        services.AddSingleton<IManagementPrincipalProvider>(f); services.AddSingleton<IManagementAuditRepository>(rows);
        services.AddSingleton<IAuditTrailSink>(f); services.Configure<ServiceAuditOptions>(o => o.Enabled = true);
        services.AddIBeamManagementAudit(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<IServiceOperationExecutor>();
        await executor.ExecuteAsync(this, _ => Task.FromResult(true), new() { OperationName = "customer.update", PermissionEnabled = false });
        Assert.IsTrue(f.Audit.Any(x => x.Action == "customer.update"));
        var calls = rows.Calls;
        await Assert.ThrowsAsync<ManagementAuditUnavailableException>(() => provider.GetRequiredService<IManagementIdentityService>().GetCurrentAsync());
        Assert.AreEqual(calls + 2, rows.Calls); // Shared executor attempts success audit then failure audit once; no recursion.
        Assert.IsTrue(typeof(IManagementAuditRepository).GetMethods().All(x => x.Name == "AppendAsync"));
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task PrivateTablePersistsAppendOnlyMetadataAndExpiryWithoutProvisioning()
    {
        var connection = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_STORAGE");
        if (string.IsNullOrWhiteSpace(connection)) Assert.Inconclusive("Private Azurite required.");
        var table = new TableClient(connection, "MgmtAudit" + Guid.NewGuid().ToString("N"));
        var repository = new AzureTableManagementAuditRepository(table);
        var row = new ManagementAuditEntity { AuditId = Guid.NewGuid(), OccurredUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7),
            OrganizationId = Guid.NewGuid(), ActorUserId = Guid.NewGuid(), ApplicationId = Guid.NewGuid(), Operation = "management.portal.errors.list", Succeeded = true, CorrelationId = Guid.NewGuid().ToString("N") };
        await Assert.ThrowsAsync<RequestFailedException>(() => repository.AppendAsync(row));
        await table.CreateIfNotExistsAsync();
        try
        {
            await repository.AppendAsync(row); await Assert.ThrowsAsync<RequestFailedException>(() => repository.AppendAsync(row));
            var stored = new List<TableEntity>(); await foreach (var item in table.QueryAsync<TableEntity>()) stored.Add(item);
            Assert.AreEqual(1, stored.Count); Assert.AreEqual(row.Operation, stored[0].GetString("Operation")); Assert.AreEqual(row.ExpiresUtc, stored[0].GetDateTimeOffset("ExpiresUtc"));
            Assert.DoesNotContain(ConnectionFixture.Key, JsonSerializer.Serialize(stored));
        }
        finally { await table.DeleteAsync(); }
    }
    [TestMethod]
    [DataRow("uid", false)]
    [DataRow("sub", false)]
    [DataRow("name-id", false)]
    [DataRow("uid", true)]
    public async Task HumanSubjectAliasesMatchServiceGuardAndConflictsAreNeverAttributed(string alias, bool conflicting)
    {
        var org = Guid.NewGuid(); var user = Guid.NewGuid();
        var claims = new List<Claim> { new(ManagementClaimTypes.SubjectType, ManagementSubjectTypes.Human), new(ManagementClaimTypes.OrganizationId, org.ToString()),
            new(alias == "name-id" ? ClaimTypes.NameIdentifier : alias, user.ToString()) };
        if (conflicting) claims.Add(new("sub", Guid.NewGuid().ToString()));
        var principal = new Principal(new(new ClaimsIdentity(claims, "Support"))); var rows = new MemoryManagementAudits();
        var sink = new ManagementAuditSink(rows, principal, new ManagementAuditContext(), Options.Create(new ManagementAuditOptions()), Options.Create(new ManagementDiagnosticsOptions()), TimeProvider.System);
        await sink.WriteTransactionAsync(new() { Action = ManagementOperations.IdentityRead, Succeeded = !conflicting });
        Assert.AreEqual(conflicting ? null : (Guid?)user, rows.Rows.Single().ActorUserId);
        Assert.AreEqual(conflicting ? "unknown" : ManagementSubjectTypes.Human, rows.Rows.Single().SubjectType);
    }
    private sealed class Principal(ClaimsPrincipal value) : IManagementPrincipalProvider { public ClaimsPrincipal GetPrincipal() => value; }
}
