using IBeam.Identity.Events;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Repositories.AzureTable;
using IBeam.Identity.Services.Tenants;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace IBeam.Tests.Management.TenantData;

[TestClass]
public sealed class MembershipOutboxRegistrationTests
{
    [TestMethod]
    [DataRow("disabled")] [DataRow("realm")] [DataRow("schema")] [DataRow("alias")]
    public async Task UnsupportedOrAmbiguousNativeStorageCannotAdvertiseAtomicEvents(string change)
    {
        await using var f = await IdentityPurgeFixture.Create(membershipEvents: true);
        switch (change) {
            case "disabled": f.NativeOptions.RequireMembershipLifecycleEvents = false; break;
            case "realm": f.NativeOptions.MembershipEventsRealmId = Guid.Empty; break;
            case "schema": f.NativeOptions.CreateTablesIfNotExists = true; break;
            default: f.NativeOptions.TenantUsersTableName = f.NativeOptions.TenantRolesTableName; break;
        }
        var validation = f.Provider.GetServices<IHostedService>().Single(x => x.GetType().DeclaringType == typeof(IdentityMembershipEventOutboxExtensions));
        await Assert.ThrowsAsync<InvalidOperationException>(() => validation.StartAsync(default));
    }
    [TestMethod]
    [DataRow("missing-delivery")] [DataRow("unsupported-provider")]
    public async Task ADefaultAuthPublisherCannotStandInForAnExplicitDurableReceiver(string change)
    {
        var services = new ServiceCollection(); services.AddSingleton<IServiceOperationExecutor>(new ServiceOperationExecutor());
        services.AddSingleton<IAuthEventPublisher>(new NoOpAuthEventPublisher());
        var store = new Mock<IIdentityMembershipEventOutboxStore>(); store.SetupGet(x => x.ProtocolVersion).Returns(change == "unsupported-provider" ? 0 : 1);
        store.SetupGet(x => x.Durable).Returns(true); services.AddSingleton(store.Object);
        if (change != "missing-delivery") { var delivery = new Mock<IIdentityMembershipEventDelivery>(); delivery.SetupGet(x => x.ProtocolVersion).Returns(1); services.AddSingleton(delivery.Object); }
        services.AddIBeamIdentityMembershipEventDispatch(o => o.Enabled = true); using var provider = services.BuildServiceProvider();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            foreach (var startup in provider.GetServices<IHostedService>().Where(x => x is not BackgroundService)) await startup.StartAsync(default);
        });
    }
    [TestMethod]
    public void DisabledDispatchInstallsNoWorkerOrReceiverAndCannotWidenNativeAuthority()
    {
        var services = new ServiceCollection(); services.AddIBeamIdentityMembershipEventDispatch(_ => { }); using var provider = services.BuildServiceProvider();
        Assert.IsNull(provider.GetService<IIdentityMembershipEventDispatcher>()); Assert.IsNull(provider.GetService<IIdentityMembershipEventDelivery>());
        Assert.HasCount(0, provider.GetServices<IHostedService>().ToArray());
    }
}
