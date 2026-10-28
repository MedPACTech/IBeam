using IBeam.Licensing;
using IBeam.Licensing.Services;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Licensing;
using IBeam.Management.Operations;
using IBeam.Management.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Moq;

namespace IBeam.Tests.Management.Licensing;

[TestClass]
public sealed class LicenseRegistrationTests
{
    [TestMethod]
    public void DisabledModuleAndPortalContractsNeverInstallOrAdvertiseTargetHandlers()
    {
        var services = new ServiceCollection(); services.AddIBeamManagementServices();
        services.AddIBeamManagementLicenseStatusTarget(_ => { });
        using var disabled = services.BuildServiceProvider();
        Assert.IsNull(disabled.GetRequiredService<ManagementOperationRegistry>().Find(ManagementLicenseOperations.Suspend));
        Assert.IsNull(disabled.GetService<ITenantLicenseService>());
        var portal = new ServiceCollection(); portal.AddIBeamManagementLicensePortal();
        using var provider = portal.BuildServiceProvider();
        var registry = provider.GetRequiredService<ManagementOperationRegistry>();
        Assert.HasCount(3, registry.Descriptors);
        Assert.HasCount(0, registry.Capabilities([ManagementLicenseOperations.Read, ManagementLicenseOperations.Suspend, ManagementLicenseOperations.Restore],
            [ManagementLicenseOperations.Read, ManagementLicenseOperations.Suspend, ManagementLicenseOperations.Restore], type => provider.GetService(type) is not null));
        Assert.IsNull(provider.GetService<ITenantLicenseService>());
    }

    [TestMethod]
    [DataRow("Production", false, false)] [DataRow("Production", true, false)]
    [DataRow("Development", false, false)] [DataRow("Testing", true, true)] [DataRow("Development", true, true)]
    public async Task VolatileNativeStorageRequiresExplicitNonProductionFixtureMode(string environment, bool allowVolatile, bool expected)
    {
        var services = Services(environment);
        services.AddIBeamManagementLicenseStatusTarget(o => { o.Enabled = true; o.AllowVolatileDevelopmentStore = allowVolatile; });
        using var provider = services.BuildServiceProvider();
        if (!expected) await Assert.ThrowsAsync<InvalidOperationException>(() => Start(provider));
        else
        {
            await Start(provider);
            var registry = provider.GetRequiredService<ManagementOperationRegistry>();
            Assert.IsNull(registry.Find(ManagementLicenseOperations.Restore));
            using var scope = provider.CreateScope();
            var grants = new[] { ManagementLicenseOperations.Read, ManagementLicenseOperations.Suspend };
            Assert.HasCount(2, registry.Capabilities(grants, grants, type => scope.ServiceProvider.GetService(type) is not null));
            Assert.AreEqual(1, scope.ServiceProvider.GetRequiredService<ITenantLicenseService>().StatusAdministrationProtocolVersion);
        }
    }

    [TestMethod]
    [DataRow("policy")] [DataRow("access")] [DataRow("store")]
    public async Task ExplicitModuleFailsStartupWhenRequiredAdaptersAreMissing(string missing)
    {
        var services = Services("Testing", missing != "access");
        if (missing == "store") services.AddSingleton<ILicensingStore>(new Mock<ILicensingStore>().Object);
        services.AddIBeamManagementLicenseStatusTarget(o => { o.Enabled = true; o.EnableRestore = true; o.AllowVolatileDevelopmentStore = true; });
        if (missing != "policy") services.AddSingleton<ILicenseStatusRestorePolicy>(new LicenseStatusTests.RestorePolicy());
        using var provider = services.BuildServiceProvider();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Start(provider));
    }

    [TestMethod]
    public async Task RegistrationNeverAutomaticallyEnablesOperationsOrAddsMachineOrHumanGrants()
    {
        var services = Services("Testing");
        services.AddSingleton<ILicenseStatusRestorePolicy>(new LicenseStatusTests.RestorePolicy());
        services.AddIBeamManagementLicenseStatusTarget(o => { o.Enabled = true; o.EnableRestore = true; o.AllowVolatileDevelopmentStore = true; });
        using var provider = services.BuildServiceProvider(); await Start(provider);
        var registry = provider.GetRequiredService<ManagementOperationRegistry>();
        using var scope = provider.CreateScope();
        var grants = new[] { ManagementLicenseOperations.Read, ManagementLicenseOperations.Suspend, ManagementLicenseOperations.Restore };
        Assert.HasCount(0, registry.Capabilities([ManagementOperations.DiagnosticsInfo], grants, _ => true));
        Assert.HasCount(0, registry.Capabilities(grants, [], _ => true));
        Assert.HasCount(3, registry.Capabilities(grants, grants, type => scope.ServiceProvider.GetService(type) is not null));
        Assert.IsFalse(registry.RequiredFor(ManagementCustomerIdentityOperations.TenantsStatus).Contains(ManagementLicenseOperations.Suspend));
    }
    private static ServiceCollection Services(string environment, bool access = true)
    {
        var services = new ServiceCollection();
        services.AddIBeamLicensingServices(new ConfigurationBuilder().Build());
        services.AddSingleton<IHostEnvironment>(new Environment(environment));
        if (access) services.AddSingleton<IManagementOperationAccessService>(new LicenseStatusTests.LiveAccess());
        return services;
    }
    private static async Task Start(ServiceProvider provider)
    { foreach (var hosted in provider.GetServices<IHostedService>()) await hosted.StartAsync(default); }
    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "ManagementLicensingFixture";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
