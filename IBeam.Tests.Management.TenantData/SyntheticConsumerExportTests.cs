using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.Entities;
using IBeam.Management.Operations;
using IBeam.Management.Services;
using IBeam.Management.Services.Audit;
using IBeam.Management.SyntheticConsumer;
using IBeam.Management.TenantData;
using IBeam.Management.TenantData.Target.Services;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace IBeam.Tests.Management.TenantData;

[TestClass]
public sealed class SyntheticConsumerExportTests
{
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task SyntheticCoverageIncludesHistoricalUnseatedAndAttachmentDataOnlyInTheCustomerChannel(bool partial)
    {
        using var f = new Engine(); await f.Submit(partial ? "partial" : "enterprise"); await f.Worker.RunAsync(f.Execution);
        var result = await f.Jobs.FindAsync(f.Execution);
        Assert.AreEqual(partial ? "partially-completed" : "succeeded", result.Job!.State);
        var names = result.Job.Coverage.Select(x => x.Key).ToArray();
        foreach (var key in new[] { "facilities", "settings", "commercial", "audit-history", "removed-member-chats", "unseated-member-chats", "audio", "attachments" }) Assert.Contains(key, names);
        Assert.DoesNotContain("PRIVATE-", JsonSerializer.Serialize(result) + JsonSerializer.Serialize(f.Audits));
        var artifact = await f.CustomerGate.RequireAsync(f.Execution, f.Customer());
        var data = Encoding.UTF8.GetString(await f.Provider.ReadForCustomerAsync(f.Execution, artifact, f.Customer()));
        Assert.Contains("PRIVATE-SYNTHETIC-removed-member-chats", data); Assert.Contains("PRIVATE-SYNTHETIC-unseated-member-chats", data);
        Assert.Contains("PRIVATE-SYNTHETIC-audio", data); Assert.Contains("PRIVATE-SYNTHETIC-attachments", data);
        Assert.AreEqual(!partial, data.Contains("PRIVATE-SYNTHETIC-commercial", StringComparison.Ordinal));
        Assert.DoesNotContain("PRIVATE-", JsonSerializer.Serialize(artifact));
    }
    [TestMethod]
    public async Task DurableConsumerReceiptsSurviveProviderRestartWithoutDuplicateOutputOrDelivery()
    {
        using var f = new Engine(); await f.Submit(); await f.Worker.RunAsync(f.Execution);
        var before = Directory.GetFiles(f.Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
        f.Restart(); await f.Submit(); await f.Worker.RunAsync(f.Execution);
        CollectionAssert.AreEqual(before, Directory.GetFiles(f.Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray());
        var artifact = await f.CustomerGate.RequireAsync(f.Execution, f.Customer());
        Assert.Contains("PRIVATE-HISTORY", Encoding.UTF8.GetString(await f.Provider.ReadForCustomerAsync(f.Execution, artifact, f.Customer())));
        Assert.AreEqual(1, f.Store.Count);
    }
    [TestMethod]
    public async Task CustomerRevocationExpiryAndExactCleanupProtectThePrivateFixtureArtifacts()
    {
        using var f = new Engine(); await f.Submit(); await f.Worker.RunAsync(f.Execution);
        f.Provider.CustomerEnabled = false;
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.CustomerGate.RequireAsync(f.Execution, f.Customer()));
        f.Provider.CustomerEnabled = true; f.Clock.Now = f.Clock.Now.AddHours(2);
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.CustomerGate.RequireAsync(f.Execution, f.Customer()));
        await f.Worker.CleanupAsync(f.Execution);
        Assert.HasCount(0, Directory.GetDirectories(f.Root)); Assert.IsTrue((await f.Store.GetAsync(f.Execution))!.Cleaned);
    }
    [TestMethod]
    public async Task UnavailableConsumerStoragePausesWithoutASecondExportAndProductionFixtureRegistrationIsRejected()
    {
        using var f = new Engine(); await f.Submit(); f.Provider.Available = false;
        await f.Worker.RunAsync(f.Execution);
        Assert.IsTrue((await f.Store.GetAsync(f.Execution))!.NeedsReconciliation);
        f.Provider.Available = true; await f.Worker.RunAsync(f.Execution);
        Assert.AreEqual("running", (await f.Jobs.FindAsync(f.Execution)).Job!.State);
        await f.Worker.RunAsync(f.Execution, reconcileUncertain: true);
        Assert.AreEqual("succeeded", (await f.Jobs.FindAsync(f.Execution)).Job!.State);
        Assert.Throws<InvalidOperationException>(() => new SyntheticTenantExporter(f.Root, "Production", f.Execution.Authority.RealmId, f.Recipient));
    }

    private sealed class Engine : IDisposable
    {
        private readonly DurableExportTests.Fixture baseline = new();
        public DurableExportTests.Clock Clock => baseline.Clock;
        public DurableExportTests.Journal Store => baseline.Store;
        public ManagementTenantExportExecution Execution { get; }
        public Guid Recipient { get; } = Guid.NewGuid();
        public string Root { get; }
        public SyntheticTenantExporter Provider { get; private set; } = null!;
        public ManagementTenantExportJobService Jobs { get; private set; } = null!;
        public ManagementTenantExportWorker Worker { get; private set; } = null!;
        public ManagementTenantExportCustomerArtifacts CustomerGate { get; private set; } = null!;
        public List<ManagementAuditEntity> Audits { get; } = [];
        private readonly ManagementTenantExportJobOptions jobOptions = new() { ActiveKeyId = "fixture", ArtifactProtectionKeys = new() { ["fixture"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) } };
        public Engine()
        {
            Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "IBeamSyntheticExportTests", Guid.NewGuid().ToString("N")));
            Execution = baseline.Execution with { Authority = baseline.Execution.Authority with { ResourceVersion = "synthetic-v1" } };
            Restart();
        }
        public void Restart()
        {
            Provider = new(Root, "Testing", Execution.Authority.RealmId, Recipient, Clock);
            var d = Options.Create(new ManagementDiagnosticsOptions { ApplicationKey = Execution.ApplicationKey, Environment = Execution.Environment });
            var t = Options.Create(new ManagementOperationTargetOptions { RealmId = Execution.Authority.RealmId, DedicatedApplicationRealm = true,
                EnabledOperations = [ManagementTenantDataOperations.Export], TrustedPortals = [new() { OrganizationId = Execution.Authority.OrganizationId, ApplicationId = Execution.Authority.ApplicationId, Issuer = "portal", Audience = "target" }] });
            var access = new Mock<IManagementOperationAccessService>(); access.SetupGet(x => x.ProtocolVersion).Returns(1);
            access.Setup(x => x.RequireAsync(ManagementTenantDataOperations.Export, It.IsAny<ManagementOperationContext>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            var audit = new Mock<IManagementAuditRepository>(); audit.Setup(x => x.AppendAsync(It.IsAny<ManagementAuditEntity>(), It.IsAny<CancellationToken>()))
                .Callback<ManagementAuditEntity, CancellationToken>((entry, _) => Audits.Add(entry)).Returns(Task.CompletedTask);
            var options = Options.Create(jobOptions); var operations = new ServiceOperationExecutor();
            Jobs = new(Store, access.Object, Provider, Provider, Options.Create(new ManagementTenantExportOptions { Enabled = true, PolicyKeys = ["enterprise", "partial"] }), options, d, t, Clock, operations);
            Worker = new(Store, Provider, Provider, Provider, audit.Object, Options.Create(new ManagementAuditOptions()), options, Clock, operations, d, t);
            CustomerGate = new(Store, Provider, Provider, options, Clock, operations, d, t);
        }
        public Task<ManagementTenantExportResult> Submit(string policy = "enterprise") => Jobs.SubmitAsync(Execution, new(policy, "synthetic-v1"));
        public ClaimsPrincipal Customer() => new(new ClaimsIdentity([new Claim("synthetic-customer", Recipient.ToString("D")), new("synthetic-tenant", Execution.Authority.TenantId.ToString("D"))], "customer-fixture"));
        public void Dispose()
        {
            var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "IBeamSyntheticExportTests")) + Path.DirectorySeparatorChar;
            if (!Root.StartsWith(parent, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidOperationException("Fixture path escaped its dedicated parent.");
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
