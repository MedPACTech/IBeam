using System.Security.Cryptography;
using System.Text.Json;
using Azure.Data.Tables;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Entities;
using IBeam.Management.Identity.Target.Services;
using IBeam.Management.Identity.Target.Services.Tenants;
using IBeam.Management.Repositories.AzureTable;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class TenantProvenanceTests
{
    [TestMethod]
    public async Task TrustedSelfServiceEvidenceIsIdempotentAndNeverChangesNativeIdentity()
    {
        var f = new Fixture(); var id = Guid.NewGuid(); var evidence = Guid.NewGuid();
        await f.Recorder.RecordSelfServiceAsync(id, evidence); await f.Recorder.RecordSelfServiceAsync(id, evidence);
        var row = await f.Metadata.GetAsync(f.Scope, id); Assert.AreEqual("self-service", row!.Source); Assert.AreEqual(evidence, row.CreateOperationId);
        Assert.HasCount(1, f.Metadata.Rows); f.Native.Verify(x => x.FindByIdAsync(id, It.IsAny<CancellationToken>()), Times.Exactly(2)); f.Native.VerifyNoOtherCalls();
        await Assert.ThrowsAsync<ManagementIdentityOperationConflictException>(() => f.Recorder.RecordSelfServiceAsync(id, Guid.NewGuid()));
    }
    [TestMethod]
    public async Task UnknownLegacyRowsRequireExplicitEvidenceAndPreserveManagementFences()
    {
        var f = new Fixture(); var id = Guid.NewGuid(); var last = Guid.NewGuid(); var evidence = Guid.NewGuid();
        await f.Metadata.TryCreateAsync(f.Row(id, "unknown", last) with { ExternalReference = "CRM:legacy" });
        await f.Recorder.RecordSelfServiceAsync(id, evidence);
        var row = (await f.Metadata.GetAsync(f.Scope, id))!; Assert.AreEqual("self-service", row.Source);
        Assert.AreEqual(last, row.LastOperationId); Assert.AreEqual("CRM:legacy", row.ExternalReference); Assert.AreEqual("ready", row.Phase);
    }
    [TestMethod, DataRow("support-provisioned", "ready"), DataRow("unknown", "renaming"), DataRow("unknown", "status-changing")]
    public async Task TrustedRecorderCannotRewriteKnownProvenanceOrInterruptCustomerMutation(string source, string phase)
    {
        var f = new Fixture(); var id = Guid.NewGuid(); var original = f.Row(id, source, Guid.NewGuid()) with { Phase = phase };
        await f.Metadata.TryCreateAsync(original);
        await Assert.ThrowsAsync<ManagementIdentityOperationConflictException>(() => f.Recorder.RecordSelfServiceAsync(id, Guid.NewGuid()));
        Assert.AreEqual(source, (await f.Metadata.GetAsync(f.Scope, id))!.Source);
    }
    [TestMethod]
    public async Task RecorderRequiresExistingTargetTenantAndDedicatedRealm()
    {
        var f = new Fixture(); var id = Guid.NewGuid(); f.Native.Setup(x => x.FindByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((IdentityTenant?)null);
        await Assert.ThrowsAsync<ManagementCustomerIdentityNotFoundException>(() => f.Recorder.RecordSelfServiceAsync(id, Guid.NewGuid()));
        await Assert.ThrowsAsync<ManagementCustomerIdentityInvalidRequestException>(() => f.Recorder.RecordSelfServiceAsync(Guid.Empty, Guid.NewGuid()));
        f.Realm.DedicatedApplicationRealm = false;
        await Assert.ThrowsAsync<ManagementCustomerIdentityUnavailableException>(() => f.Recorder.RecordSelfServiceAsync(Guid.NewGuid(), Guid.NewGuid()));
        Assert.HasCount(0, f.Metadata.Rows);
    }
    [TestMethod]
    public async Task DirectoryReadsEnrichOneBoundedPageAndLeaveLegacyRowsUnknownWithoutWriting()
    {
        var f = new Fixture(); var rows = new TenantRows(); var id = Guid.Parse(rows.Rows[0].RowKey);
        await f.Recorder.RecordSelfServiceAsync(id, Guid.NewGuid());
        f.Actor.Actor = f.Actor.Actor with { Operation = ManagementCustomerIdentityOperations.TenantsRead };
        var service = f.Directory(rows); var page = await service.ListAsync(new(1));
        Assert.HasCount(1, page.Items); Assert.AreEqual("self-service", page.Items[0].Source); Assert.AreEqual(1, rows.Reads);
        var next = await service.ListAsync(new(1, page.ContinuationToken)); Assert.AreEqual("unknown", next.Items[0].Source);
        Assert.AreEqual("self-service", (await service.FindAsync(id))!.Source); Assert.HasCount(1, f.Metadata.Rows);
    }
    [TestMethod]
    public async Task SupportCreateRecordsEvidenceAndSelfServiceMetadataSurvivesRename()
    {
        var f = new TenantAdministrationFixture(); var created = await f.Service.CreateAsync(new() { Name = "Enterprise" }, "provenance-create-01");
        Assert.AreEqual("support-provisioned", created.Source); Assert.AreEqual("support-provisioned", created.Tenant!.Source);
        var row = f.Metadata.Rows.Values.Single(); Assert.AreEqual(created.Operation.OperationId, row.CreateOperationId);
        f.Metadata.Rows[f.Metadata.Rows.Keys.Single()] = row with { Source = "self-service" };
        f.UpdateAuthority(); var changed = await f.Service.RenameAsync(row.TenantId, new() { Name = "Renamed" }, created.Tenant.Version!, "provenance-rename-01");
        Assert.AreEqual("self-service", changed.Source); Assert.AreEqual("self-service", changed.Tenant!.Source);
    }
    [TestMethod]
    public void PublicCreateCannotChooseProvenanceAndSafeDefaultsNeverInferSelfSignup()
    {
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<CreateManagementCustomerTenant>("{\"Name\":\"Example\",\"Source\":\"self-service\"}"));
        Assert.AreEqual("unknown", new ManagementCustomerTenantDto(Guid.NewGuid(), "Legacy", "Active", null, null, "v1").Source);
    }
    [TestMethod, TestCategory("StorageIntegration")]
    public async Task ActualProviderKeepsOverlappingTenantEvidenceIsolatedAcrossIdentityStorage()
    {
        var one = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_STORAGE"); var two = Environment.GetEnvironmentVariable("IBEAM_MANAGEMENT_TEST_SEPARATE_STORAGE");
        if (string.IsNullOrWhiteSpace(one) || string.IsNullOrWhiteSpace(two)) Assert.Inconclusive("Two private Azurite instances required.");
        var name = "Provenance" + Guid.NewGuid().ToString("N"); var first = new TableClient(one, name); var second = new TableClient(two, name);
        try {
            await first.CreateIfNotExistsAsync(); await second.CreateIfNotExistsAsync();
            var f = new Fixture(); var scope = f.Scope; var tenant = Guid.NewGuid(); var evidence = Guid.NewGuid();
            var a = new AzureTableManagementTenantAdministrationRepository(first); var b = new AzureTableManagementTenantAdministrationRepository(second);
            var recorder = f.CreateRecorder(a);
            await Task.WhenAll(recorder.RecordSelfServiceAsync(tenant, evidence), recorder.RecordSelfServiceAsync(tenant, evidence));
            Assert.AreEqual("self-service", (await a.GetAsync(scope, tenant))!.Source); Assert.IsNull(await b.GetAsync(scope, tenant));
            Assert.IsNull(await a.GetAsync(scope with { IdentityRealmId = Guid.NewGuid() }, tenant));
        } finally { await first.DeleteAsync(); await second.DeleteAsync(); }
    }
    private sealed class Fixture
    {
        public OperationFixture Actor { get; } = new();
        public TenantMetadataStore Metadata { get; } = new();
        public Mock<IIdentityTenantStore> Native { get; } = new();
        public ManagementCustomerIdentityTargetOptions Realm { get; }
        public ManagementDiagnosticsOptions Deployment { get; }
        public ManagementTenantAdministrationScope Scope => new(Realm.IdentityRealmId, Deployment.ApplicationKey, Deployment.Environment);
        public Fixture() {
            Realm = new() { DedicatedApplicationRealm = true, IdentityRealmId = Actor.Actor.IdentityRealmId };
            Deployment = new() { ApplicationKey = Actor.Actor.ApplicationKey, Environment = Actor.Actor.Environment };
            Native.Setup(x => x.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid id, CancellationToken _) => new IdentityTenant(id, "Customer"));
        }
        public IManagementTenantProvenanceRecorder Recorder => CreateRecorder(Metadata);
        public IManagementTenantProvenanceRecorder CreateRecorder(IManagementTenantAdministrationRepository repository)
            => new ManagementTenantProvenanceRecorder(repository, Native.Object, Options.Create(Realm), Options.Create(Deployment), new ServiceOperationExecutor());
        public ManagementTenantAdministrationEntity Row(Guid id, string source, Guid last) => new() {
            IdentityRealmId = Scope.IdentityRealmId, ApplicationKey = Scope.ApplicationKey, Environment = Scope.Environment,
            TenantId = id, Source = source, LastOperationId = last, Phase = "ready" };
        public ManagementTenantDirectoryService Directory(TenantRows rows) => new(rows, Actor,
            Options.Create(new ManagementTenantDirectoryOptions { ProtectionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }),
            Options.Create(Realm), TimeProvider.System, new ServiceOperationExecutor(), Metadata);
    }
}
