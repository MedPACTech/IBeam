using IBeam.Identity.Events;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Tenants;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
namespace IBeam.Tests.Identity.Services;

[TestClass]
public sealed class IdentityTenantAdministrationTests
{
    [TestMethod]
    public async Task ExternalCreateRedactsLifecycleFailuresAndNeverSuppliesACustomerActor()
    {
        const string canary = "PRIVATE-PROFILE-EXCEPTION-CANARY";
        var store = new Mock<IIdentityTenantStore>(); var hooks = new Mock<ITenantExtensionCoordinator>();
        store.Setup(x => x.CreateAsync(It.IsAny<IdentityTenant>(), It.IsAny<CancellationToken>())).ReturnsAsync((IdentityTenant t, CancellationToken _) => t);
        TenantExtensionContext? observed = null;
        hooks.Setup(x => x.OnTenantCreatedAsync(It.IsAny<IdentityTenant>(), It.IsAny<TenantExtensionContext>(), It.IsAny<CancellationToken>()))
            .Callback((IdentityTenant _, TenantExtensionContext c, CancellationToken _) => observed = c).Returns(Task.CompletedTask);
        var publisher = new Mock<IAuthEventPublisher>();
        publisher.Setup(x => x.PublishAsync(It.IsAny<TenantCreatedEvent>(), It.IsAny<CancellationToken>())).ThrowsAsync(new Exception(canary));
        var lifecycle = Mock.Of<IAuthLifecycleHook>(); var options = Options.Create(new AuthEventOptions { StrictPublishFailures = false });
        var logger = new RecordingLogger();
        var normal = new IdentityTenantService(store.Object, hooks.Object, publisher.Object, lifecycle, options, logger);
        var service = new IdentityTenantAdministrationService(normal, store.Object, hooks.Object, lifecycle, publisher.Object, options,
            NullLogger<IdentityTenantAdministrationService>.Instance, new ServiceOperationExecutor());
        var failure = await Assert.ThrowsAsync<IdentityProviderException>(() => service.CreateAsync("Enterprise", Guid.NewGuid(), TenantExtensionContext.Create("created", Guid.NewGuid())));
        Assert.DoesNotContain(canary, failure.ToString()); Assert.IsNotNull(observed); Assert.IsNull(observed.AuthUserId); Assert.IsTrue(observed.RedactFailureDetails);
        Assert.IsTrue(observed.RequireLifecycleDelivery);
        Assert.HasCount(1, logger.Entries); Assert.IsNull(logger.Entries[0].Exception); Assert.DoesNotContain(canary, logger.Entries[0].Message);
        Assert.IsFalse(service.SupportsConditionalUpdates);
    }
    [TestMethod, DataRow(true), DataRow(false)]
    public async Task OrdinaryIdentityLifecycleLoggingKeepsItsExistingDefaultBehavior(bool strict)
    {
        var store = new Mock<IIdentityTenantStore>(); store.Setup(x => x.CreateAsync(It.IsAny<IdentityTenant>(), It.IsAny<CancellationToken>())).ReturnsAsync((IdentityTenant t, CancellationToken _) => t);
        var publisher = new Mock<IAuthEventPublisher>(); var exception = new Exception("ordinary-error");
        publisher.Setup(x => x.PublishAsync(It.IsAny<TenantCreatedEvent>(), It.IsAny<CancellationToken>())).ThrowsAsync(exception);
        var logger = new RecordingLogger();
        var service = new IdentityTenantService(store.Object, Mock.Of<ITenantExtensionCoordinator>(), publisher.Object, Mock.Of<IAuthLifecycleHook>(),
            Options.Create(new AuthEventOptions { StrictPublishFailures = strict }), logger);
        if (strict) await Assert.ThrowsAsync<Exception>(() => service.CreateAsync("Ordinary"));
        else Assert.AreEqual("Ordinary", (await service.CreateAsync("Ordinary")).Name);
        Assert.AreSame(exception, logger.Entries.Single().Exception);
    }
    private sealed class RecordingLogger : ILogger<IdentityTenantService>
    {
        public List<(Exception? Exception, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((exception, formatter(state, exception)));
    }
}
