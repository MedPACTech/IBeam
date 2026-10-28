using IBeam.Management;
using IBeam.Management.Operations;
using IBeam.Management.Services;
using IBeam.Management.TenantData;
using IBeam.Management.TenantData.Target.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;

namespace IBeam.Tests.Management.TenantData;

[TestClass]
public sealed class PurgeCoordinatorRegistrationTests
{
    [TestMethod]
    [DataRow("repository")] [DataRow("fence")] [DataRow("unsupported-fence")] [DataRow("duplicate-fence")]
    [DataRow("volatile-production")] [DataRow("unsupported-store")]
    public async Task StandardCoordinatorRequiresDurableStorageAndEveryCompatibleNativeFence(string missing)
    {
        using var provider = Providers(missing);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { foreach (var hosted in provider.GetServices<IHostedService>()) if (hosted is not BackgroundService) await hosted.StartAsync(default); });
    }
    [TestMethod]
    public async Task CompleteExplicitDevelopmentRegistrationInstallsTheStandardWorkerAndJobService()
    {
        using var p = Providers();
        foreach (var hosted in p.GetServices<IHostedService>()) if (hosted is not BackgroundService) await hosted.StartAsync(default);
        using var scope = p.CreateScope();
        Assert.IsInstanceOfType<ManagementTenantPurgeJobService>(scope.ServiceProvider.GetRequiredService<IManagementTenantPurgeJobService>());
        Assert.IsInstanceOfType<ManagementTenantPurgeWorker>(scope.ServiceProvider.GetRequiredService<IManagementTenantPurgeWorker>());
    }
    [TestMethod]
    public async Task DisabledRecoveryDoesNotReadStorageOrDispatchAnyWork()
    {
        var services = new ServiceCollection(); using var p = services.BuildServiceProvider();
        var scanner = new ManagementTenantPurgeBackgroundWorker(p.GetRequiredService<IServiceScopeFactory>(), Options.Create(new ManagementTenantPurgeJobOptions()),
            Options.Create(new ManagementDiagnosticsOptions()), Options.Create(new ManagementOperationTargetOptions()));
        await scanner.ScanAsync(); scanner.Dispose();
    }
    [TestMethod]
    public async Task RecoveryRotatesBindingsAndSkipsUncertainOrCompletedJobs()
    {
        var realm = Guid.NewGuid(); var org = Guid.NewGuid(); var app1 = Guid.NewGuid(); var app2 = Guid.NewGuid(); var tenant = Guid.NewGuid(); var id = Guid.NewGuid();
        var execution = new ManagementTenantPurgeExecution(new(org, app1, realm, tenant, Guid.NewGuid(), id) { ResourceId = tenant }, "fixture", "Testing", id, "enterprise", "fixture-inventory");
        var store = new Mock<IManagementTenantPurgeJournalRepository>(); var worker = new Mock<IManagementTenantPurgeWorker>();
        var seen = new List<Guid>(); var state = "unknown";
        store.Setup(x => x.ListPageAsync(It.IsAny<ManagementTenantPurgeDeployment>(), 10, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns((ManagementTenantPurgeDeployment d, int _, string? _, CancellationToken _) => { seen.Add(d.ApplicationId); return Task.FromResult(new ManagementTenantPurgeJobPage([execution], null)); });
        store.Setup(x => x.GetJobAsync(It.IsAny<ManagementTenantPurgeExecution>(), It.IsAny<CancellationToken>())).Returns(() => Task.FromResult<ManagementTenantPurgeJournalEntity?>(new() {
            Kind = "job", Execution = execution, Definition = Definition, Plan = new(1, tenant, Guid.NewGuid(), "enterprise", "v1", "fixture-inventory", "", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1), []),
            IntentHash = "fixture", CreatedUtc = DateTimeOffset.UtcNow, UpdatedUtc = DateTimeOffset.UtcNow, FenceKeys = ["app-fence"],
            State = state, Phase = state == "succeeded" ? "done" : "purging", NeedsReconciliation = state == "unknown" }));
        worker.Setup(x => x.RunAsync(It.IsAny<ManagementTenantPurgeExecution>(), false, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var services = new ServiceCollection(); services.AddSingleton(store.Object); services.AddSingleton(worker.Object); using var p = services.BuildServiceProvider();
        var scanner = new ManagementTenantPurgeBackgroundWorker(p.GetRequiredService<IServiceScopeFactory>(), Options.Create(new ManagementTenantPurgeJobOptions { EnableBackgroundWorker = true }),
            Options.Create(new ManagementDiagnosticsOptions { ApplicationKey = "fixture", Environment = "Testing" }), Options.Create(new ManagementOperationTargetOptions { RealmId = realm,
                EnabledOperations = [ManagementTenantDataOperations.Delete], TrustedPortals = [new() { OrganizationId = org, ApplicationId = app1 }, new() { OrganizationId = org, ApplicationId = app2 }] }));
        await scanner.ScanAsync(); state = "succeeded"; await scanner.ScanAsync();
        Assert.AreEqual(app1, seen[0]); Assert.AreEqual(app2, seen[1]);
        worker.Verify(x => x.RunAsync(It.IsAny<ManagementTenantPurgeExecution>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        state = "requested"; await scanner.ScanAsync();
        worker.Verify(x => x.RunAsync(execution, false, It.IsAny<CancellationToken>()), Times.Once); scanner.Dispose();
    }
    private static ManagementTenantPurgeDefinition Definition => new(1, "fixture", 1, Array.AsReadOnly(new[] {
        new ManagementTenantPurgeCategory("app-data", "dataset", "delete"), new("global-accounts", "identity-metadata", "exclude") }));
    private static ServiceProvider Providers(string missing = "")
    {
        var services = new ServiceCollection(); services.AddSingleton<IHostEnvironment>(new Host(missing == "volatile-production" ? "Production" : "Testing"));
        services.AddIBeamManagementTenantPurgeTarget(o => { o.Enabled = true; o.PolicyKeys = ["enterprise"]; o.AllowVolatileDevelopmentJobs = true; });
        services.AddIBeamManagementTenantPurgeJobs(new ConfigurationBuilder().Build());
        var access = new Mock<IManagementOperationAccessService>(); access.SetupGet(x => x.ProtocolVersion).Returns(1); services.AddSingleton(access.Object);
        var handler = new Mock<IManagementTenantPurgeHandler>(); handler.SetupGet(x => x.ProtocolVersion).Returns(1); handler.Setup(x => x.Describe()).Returns(Definition); services.AddSingleton(handler.Object);
        var policy = new Mock<IManagementTenantPurgePolicy>(); policy.SetupGet(x => x.ProtocolVersion).Returns(1); services.AddSingleton(policy.Object);
        var audit = new Mock<IManagementAuditRepository>(); services.AddSingleton(audit.Object);
        if (missing != "repository") {
            var repository = new Mock<IManagementTenantPurgeJournalRepository>(); repository.SetupGet(x => x.ProtocolVersion).Returns(missing == "unsupported-store" ? 99 : 1);
            repository.SetupGet(x => x.Durable).Returns(false); services.AddSingleton(repository.Object);
        }
        if (missing != "fence") {
            var fence = new Mock<IManagementTenantPurgeAccessFence>(); fence.SetupGet(x => x.ProtocolVersion).Returns(missing == "unsupported-fence" ? 99 : 1); fence.SetupGet(x => x.Key).Returns("app-fence");
            services.AddSingleton(fence.Object); if (missing == "duplicate-fence") services.AddSingleton(fence.Object);
        }
        return services.BuildServiceProvider();
    }
    private sealed class Host(string environment) : IHostEnvironment {
        public string EnvironmentName { get; set; } = environment; public string ApplicationName { get; set; } = "fixture";
        public string ContentRootPath { get; set; } = "."; public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
