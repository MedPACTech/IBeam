using IBeam.Licensing;
using IBeam.Licensing.Services;
using IBeam.Management;
using IBeam.Management.Licensing;
using IBeam.Management.Operations;
using IBeam.Management.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace IBeam.Tests.Management.Licensing;

[TestClass]
public sealed class SeatRegistrationTests
{
    [TestMethod]
    public void AbsentAndPortalOnlyModulesInstallNoNativeSeatCapability()
    {
        var services = new ServiceCollection(); services.AddIBeamManagementServices(); services.AddIBeamManagementSeatsTarget(_ => { });
        using var absent = services.BuildServiceProvider(); Assert.IsNull(absent.GetRequiredService<ManagementOperationRegistry>().Find(ManagementSeatOperations.Assign));
        var portal = new ServiceCollection(); portal.AddIBeamManagementSeatsPortal(); using var provider = portal.BuildServiceProvider();
        var registry = provider.GetRequiredService<ManagementOperationRegistry>(); var grants = Grants;
        Assert.HasCount(0, registry.Capabilities(grants, grants, type => provider.GetService(type) is not null));
        Assert.IsNull(provider.GetService<ILicenseSeatAssignmentService>());
    }
    [TestMethod]
    [DataRow("Production", true, true, false)] [DataRow("Development", false, true, false)]
    [DataRow("Testing", true, false, false)] [DataRow("Testing", true, true, true)]
    public async Task ExplicitRegistrationRequiresDurabilityOrApprovedFixtureModeAndSubjectPolicy(string environment, bool allowVolatile, bool policy, bool expected)
    {
        var services = new ServiceCollection(); services.AddIBeamLicensingServices(new ConfigurationBuilder().Build());
        services.AddSingleton<IHostEnvironment>(new HostEnvironment(environment)); services.AddSingleton<IManagementOperationAccessService>(new SeatAdministrationTests.Access());
        if (policy) services.AddSingleton<ILicenseSeatSubjectPolicy>(new SeatAdministrationTests.Subjects());
        services.AddIBeamManagementSeatsTarget(o => { o.Enabled = true; o.AllowVolatileDevelopmentStore = allowVolatile; });
        using var provider = services.BuildServiceProvider();
        async Task Start() { foreach (var hosted in provider.GetServices<IHostedService>()) await hosted.StartAsync(default); }
        if (!expected) await Assert.ThrowsAsync<InvalidOperationException>(Start);
        else
        {
            await Start(); using var scope = provider.CreateScope(); var registry = provider.GetRequiredService<ManagementOperationRegistry>();
            Assert.HasCount(3, registry.Capabilities(Grants, Grants, type => scope.ServiceProvider.GetService(type) is not null));
            Assert.HasCount(0, registry.Capabilities([ManagementOperations.DiagnosticsInfo], Grants, _ => true));
            Assert.HasCount(0, registry.Capabilities(Grants, [], _ => true));
            Assert.AreEqual(1, scope.ServiceProvider.GetRequiredService<ILicenseSeatAssignmentService>().SeatAdministrationProtocolVersion);
        }
    }
    internal static string[] Grants => [ManagementSeatOperations.Read, ManagementSeatOperations.Assign, ManagementSeatOperations.Unassign];
    private sealed class HostEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "SeatFixture";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
