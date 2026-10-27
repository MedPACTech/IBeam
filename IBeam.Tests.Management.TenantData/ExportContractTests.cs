using System.Security.Claims;
using System.Text;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.Operations;
using IBeam.Management.Services;
using IBeam.Management.Services.CustomerIdentity.Delegation;
using IBeam.Management.TenantData;
using IBeam.Management.TenantData.Target.Api;
using IBeam.Management.TenantData.Target.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Moq;

namespace IBeam.Tests.Management.TenantData;

[TestClass]
public sealed class ExportContractTests
{
    internal static ManagementTenantExportDefinition Definition => new(1, "synthetic-export", 1, "point-in-time", Array.AsReadOnly(new[] {
        new ManagementTenantExportDataset("facilities", "dataset"), new("settings", "dataset"), new("audio", "attachment") }));
    internal static ManagementTenantExportResult Result(Guid tenant, Guid job, string state = "requested")
    {
        var now = DateTimeOffset.UtcNow;
        return new(tenant, job, "accepted", new(1, tenant, job, state, now, now, now.AddHours(1), "point-in-time",
            Definition.Datasets.Select(x => new ManagementTenantExportCoverage(x.Key, x.Kind, state == "succeeded" ? "completed" : "pending")).ToArray(), null));
    }
    [TestMethod]
    [DataRow("requested")] [DataRow("running")] [DataRow("succeeded")] [DataRow("failed")] [DataRow("cancelled")] [DataRow("expired")]
    public void JobStatesAreDistinctFromSubmissionAcceptanceAndPayloadSchemaRemainsBounded(string state)
    {
        var tenant = Guid.NewGuid(); var job = Guid.NewGuid(); var input = Result(tenant, job, state);
        var result = ManagementTenantExportProtocol.Result(input, tenant, job, Definition);
        Assert.AreEqual("accepted", result.Outcome); Assert.AreEqual(state, result.Job!.State);
        var registry = new ManagementOperationRegistry([ManagementTenantExportServiceExtensions.Descriptor]);
        var codec = new ManagementOperationPayloadCodec(registry);
        var bytes = codec.WriteResult(ManagementTenantDataOperations.Export, result);
        Assert.AreEqual(state, codec.ReadResult<ManagementTenantExportResult>(ManagementTenantDataOperations.Export, bytes).Job!.State);
        Assert.IsTrue(bytes.Length < 16384);
    }
    [TestMethod]
    [DataRow("missing")] [DataRow("duplicate")] [DataRow("undeclared")] [DataRow("kind")]
    [DataRow("pending-success")] [DataRow("omitted-success")] [DataRow("arbitrary-error")]
    [DataRow("foreign-tenant")] [DataRow("foreign-job")] [DataRow("unknown-state")]
    public void InvalidOrIncompleteCoverageCannotBecomeSuccessfulMetadata(string change)
    {
        var tenant = Guid.NewGuid(); var job = Guid.NewGuid(); var result = Result(tenant, job, "succeeded"); var status = result.Job!;
        status = change switch {
            "missing" => status with { Coverage = status.Coverage[..2] },
            "duplicate" => status with { Coverage = [status.Coverage[0], status.Coverage[0], status.Coverage[2]] },
            "undeclared" => status with { Coverage = [status.Coverage[0] with { Key = "PRIVATE-CUSTOMER-FILENAME" }, ..status.Coverage[1..]] },
            "kind" => status with { Coverage = [status.Coverage[0] with { Kind = "attachment" }, ..status.Coverage[1..]] },
            "pending-success" => status with { Coverage = [status.Coverage[0] with { Disposition = "pending" }, ..status.Coverage[1..]] },
            "omitted-success" => status with { Coverage = [status.Coverage[0] with { Disposition = "omitted" }, ..status.Coverage[1..]] },
            "arbitrary-error" => status with { FailureCode = "PRIVATE-PROVIDER-ERROR" },
            "foreign-tenant" => status with { TenantId = Guid.NewGuid() },
            "foreign-job" => status with { JobId = Guid.NewGuid() },
            _ => status with { State = "done-ish" }
        };
        var error = Assert.Throws<ManagementOperationUnavailableException>(() => ManagementTenantExportProtocol.Result(result with { Job = status }, tenant, job, Definition));
        Assert.DoesNotContain("PRIVATE-", error.ToString());
    }
    [TestMethod]
    public void OmittedAndRetainedDatasetsAreExplicitlyPartialAndMetadataIsDefensivelyCopied()
    {
        var tenant = Guid.NewGuid(); var job = Guid.NewGuid(); var result = Result(tenant, job, "succeeded");
        var source = result.Job!.Coverage; source[0] = source[0] with { Disposition = "retained" }; source[2] = source[2] with { Disposition = "omitted" };
        var safe = ManagementTenantExportProtocol.Result(result with { Job = result.Job with { State = "partially-completed" } }, tenant, job, Definition);
        source[0] = new("PRIVATE-CHANGED", "dataset", "completed");
        Assert.AreEqual("facilities", safe.Job!.Coverage[0].Key); Assert.AreEqual("partially-completed", safe.Job.State);
        var unknown = ManagementTenantExportProtocol.Result(new(tenant, job, "unknown", null), tenant, job, Definition);
        Assert.IsNull(unknown.Job);
        Assert.Throws<ManagementOperationUnavailableException>(() => ManagementTenantExportProtocol.Result(unknown with { Job = safe.Job }, tenant, job, Definition));
    }
    [TestMethod]
    public void PrivateArtifactsAndOutputCannotSerializeOrPrintTheirReferences()
    {
        const string secret = "https://private.example/PRIVATE-ARTIFACT?signature=PRIVATE-TICKET";
        var artifact = new ManagementTenantExportArtifact(secret, DateTimeOffset.UtcNow.AddHours(1));
        var output = new ManagementTenantExportOutput(artifact, [new("PRIVATE-UNVALIDATED", "dataset", "completed")]);
        Assert.DoesNotContain("PRIVATE-", JsonSerializer.Serialize(artifact));
        Assert.DoesNotContain("PRIVATE-", JsonSerializer.Serialize(output));
        Assert.DoesNotContain("PRIVATE-", artifact.ToString() + output);
        Assert.DoesNotContain("IBeam.Identity", string.Join(',', typeof(ManagementTenantExportRequest).Assembly.GetReferencedAssemblies().Select(x => x.Name)));
        Assert.DoesNotContain("IBeam.Licensing", string.Join(',', typeof(ManagementTenantExportRequest).Assembly.GetReferencedAssemblies().Select(x => x.Name)));
    }
    [TestMethod]
    public void CompiledExportRoutesCannotBecomeAnArbitraryNativeProxy()
    {
        var descriptor = ManagementTenantExportServiceExtensions.Descriptor;
        foreach (var invalid in new[] { descriptor with { RouteTemplate = "/api/management-tenant-data/anything" }, descriptor with { Method = "DELETE" },
            descriptor with { Operation = "management.some.other" }, descriptor with { RouteTemplate = "/api/management-operations/custom/export" } })
            Assert.Throws<ArgumentException>(() => new ManagementOperationRegistry([invalid]));
        var tenant = Guid.NewGuid(); var job = Guid.NewGuid(); var path = ManagementTenantExportRoutes.StatusPath(tenant, job);
        Assert.IsTrue(ManagementTenantExportRoutes.IsStatusPath(path));
        var request = new IBeam.Management.CustomerIdentity.ManagementDelegationRequest { Method = "GET", PathAndQuery = path };
        Assert.HasCount(64, ManagementDelegationBinding.HashRegisteredReceipt(request));
        foreach (var invalid in new[] { path + "?download=true", path.Replace(job.ToString("D"), ".."), path.Replace(tenant.ToString("D"), Guid.Empty.ToString("D")), "https://private.example" })
        {
            Assert.IsFalse(ManagementTenantExportRoutes.IsStatusPath(invalid));
            Assert.Throws<ManagementAccessDeniedException>(() => ManagementDelegationBinding.HashRegisteredReceipt(new() { Method = "GET", PathAndQuery = invalid }));
        }
    }
    [TestMethod]
    public void DefaultDisabledModulesInstallNeitherAuthorityNorApiScheme()
    {
        var services = new ServiceCollection(); services.AddIBeamManagementServices();
        services.AddIBeamManagementTenantDataTargetApi(new ConfigurationBuilder().Build(), _ => { });
        using var provider = services.BuildServiceProvider();
        Assert.IsNull(provider.GetRequiredService<ManagementOperationRegistry>().Find(ManagementTenantDataOperations.Export));
        Assert.IsNull(provider.GetService<IManagementOperationHandler<ManagementTenantExportRequest, ManagementTenantExportResult>>());
    }
    [TestMethod]
    [DataRow("handler")] [DataRow("jobs")] [DataRow("delivery")] [DataRow("customer")] [DataRow("policy")]
    [DataRow("volatile-production")] [DataRow("unsupported-handler")]
    public async Task ExportCannotBeInstalledWithoutAllUsableHostOwnedProviders(string missing)
    {
        using var provider = Providers(missing);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { foreach (var hosted in provider.GetServices<IHostedService>()) await hosted.StartAsync(default); });
    }
    [TestMethod]
    public async Task CompatibleExplicitModuleAdvertisesOnlyItsIndependentPermissionAndNeverExecutesDuringDiscovery()
    {
        using var provider = Providers();
        foreach (var hosted in provider.GetServices<IHostedService>()) await hosted.StartAsync(default);
        using var scope = provider.CreateScope(); var registry = provider.GetRequiredService<ManagementOperationRegistry>();
        Assert.HasCount(1, registry.Capabilities([ManagementTenantDataOperations.Export], [ManagementTenantDataOperations.Export], type => scope.ServiceProvider.GetService(type) is not null));
        Assert.HasCount(0, registry.Capabilities([IBeam.Management.CustomerIdentity.ManagementCustomerIdentityOperations.TenantsStatus], [ManagementTenantDataOperations.Export], _ => true));
        Assert.IsFalse(registry.Contains(ManagementTenantDataOperations.Delete));
    }
    private static ServiceProvider Providers(string missing = "")
    {
        var services = new ServiceCollection(); services.AddIBeamManagementServices(); services.AddSingleton<IHostEnvironment>(new Host(missing == "volatile-production" ? "Production" : "Testing"));
        services.Configure<ManagementDiagnosticsOptions>(o => { o.ApplicationKey = "fixture"; o.Environment = "Testing"; });
        services.AddIBeamManagementTenantExportTarget(o => { o.Enabled = true; o.PolicyKeys = ["enterprise"]; o.AllowVolatileDevelopmentJobs = true; });
        var access = new Mock<IManagementOperationAccessService>(); access.SetupGet(x => x.ProtocolVersion).Returns(1); services.AddSingleton(access.Object);
        if (missing != "handler") { var handler = new Mock<IManagementTenantExportHandler>(); handler.SetupGet(x => x.ProtocolVersion).Returns(missing == "unsupported-handler" ? 99 : 1); handler.Setup(x => x.Describe()).Returns(Definition); services.AddSingleton(handler.Object); }
        if (missing != "jobs") { var jobs = new Mock<IManagementTenantExportJobService>(); jobs.SetupGet(x => x.ProtocolVersion).Returns(1); jobs.SetupGet(x => x.Durable).Returns(false); services.AddSingleton(jobs.Object); }
        if (missing != "delivery") { var delivery = new Mock<IManagementTenantExportDelivery>(); delivery.SetupGet(x => x.ProtocolVersion).Returns(1); services.AddSingleton(delivery.Object); }
        if (missing != "customer") { var customer = new Mock<IManagementTenantExportCustomerAuthorization>(); customer.SetupGet(x => x.ProtocolVersion).Returns(1); services.AddSingleton(customer.Object); }
        if (missing != "policy") { var policy = new Mock<IManagementTenantExportPolicy>(); policy.SetupGet(x => x.ProtocolVersion).Returns(1); services.AddSingleton(policy.Object); }
        return services.BuildServiceProvider();
    }
    private sealed class Host(string name) : IHostEnvironment
    { public string EnvironmentName { get; set; } = name; public string ApplicationName { get; set; } = "ExportFixture"; public string ContentRootPath { get; set; } = "."; public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider(); }
}
