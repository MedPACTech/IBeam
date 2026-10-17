using System.Security.Claims;
using IBeam.Management;
using IBeam.Management.Services;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Management;

[TestClass]
public sealed class ManagementServiceBoundaryTests
{
    [TestMethod]
    public async Task DirectServiceCallsEnforcePermissionsAndUseOperationAuditForDenials()
    {
        var principal = new MutablePrincipalProvider();
        var sink = new RecordingAuditSink();
        var services = new ServiceCollection();
        services.AddIBeamManagementServices();
        services.AddSingleton<IManagementPrincipalProvider>(principal);
        services.AddSingleton<IAuditTrailSink>(sink);
        services.Configure<ServiceAuditOptions>(o => o.Enabled = true);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IManagementIdentityService>();

        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => service.GetCurrentAsync());
        principal.Principal = new ClaimsPrincipal(new ClaimsIdentity(ManagementApiBoundaryTests.SupportClaims(), "TrustedSupport"));
        var result = await service.GetCurrentAsync();
        Assert.AreEqual(ManagementApiBoundaryTests.OrganizationId, result.ManagementOrganizationId);
        Assert.HasCount(2, sink.Transactions);
        Assert.IsFalse(sink.Transactions[0].Succeeded);
        Assert.IsTrue(sink.Transactions[1].Succeeded);
        Assert.IsTrue(sink.Transactions.All(x => x.Action == ManagementOperations.IdentityRead));
        Assert.IsTrue(sink.Transactions.All(x => x.BeforeJson is null && x.AfterJson is null));
    }

    [TestMethod]
    public async Task ContextCannotBeAssembledAcrossIdentitiesOrUnauthenticatedClaims()
    {
        var principal = new MutablePrincipalProvider();
        var service = new ManagementIdentityService(principal, new ServiceOperationExecutor());
        var support = ManagementApiBoundaryTests.SupportClaims();
        principal.Principal = new ClaimsPrincipal(new[]
        {
            new ClaimsIdentity(support.Where(c => c.Type == "sub"), "Customer"),
            new ClaimsIdentity(support.Where(c => c.Type != "sub"))
        });
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => service.GetCurrentAsync());
        principal.Principal = new ClaimsPrincipal(new[]
        {
            new ClaimsIdentity(support, "Support"),
            new ClaimsIdentity([new Claim("sub", "other")], "AnotherScheme")
        });
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => service.GetCurrentAsync());
    }

    [TestMethod]
    public async Task DirectTargetServiceRejectsHumanAndOtherDeployment()
    {
        var principal = new MutablePrincipalProvider();
        var service = new ManagementDiagnosticsInfoService(principal, new ServiceOperationExecutor(),
            Options.Create(new ManagementDiagnosticsOptions { ApplicationKey = "qurvia", Environment = "Production" }));
        principal.Principal = new ClaimsPrincipal(new ClaimsIdentity(ManagementApiBoundaryTests.SupportClaims(), "Support"));
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => service.GetAsync());
        principal.Principal = new ClaimsPrincipal(new ClaimsIdentity(ManagementApiBoundaryTests.KeyClaims(), "ManagementKey"));
        var result = await service.GetAsync();
        Assert.AreEqual("qurvia", result.ApplicationKey);
        Assert.HasCount(1, result.Capabilities);
        principal.Principal = new ClaimsPrincipal(new ClaimsIdentity(ManagementApiBoundaryTests.KeyClaims().Select(
            c => c.Type == ManagementClaimTypes.ApplicationKey ? new Claim(c.Type, "bindry") : c), "ManagementKey"));
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => service.GetAsync());
    }

    [TestMethod]
    public async Task ServiceHonorsRequestCancellation()
    {
        var principal = new MutablePrincipalProvider
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(ManagementApiBoundaryTests.SupportClaims(), "Support"))
        };
        var service = new ManagementIdentityService(principal, new ServiceOperationExecutor());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.GetCurrentAsync(cancelled.Token));
    }

    private sealed class MutablePrincipalProvider : IManagementPrincipalProvider
    {
        public ClaimsPrincipal Principal { get; set; } = new(new ClaimsIdentity());
        public ClaimsPrincipal GetPrincipal() => Principal;
    }

    private sealed class RecordingAuditSink : IAuditTrailSink
    {
        public List<ServiceAuditTransaction> Transactions { get; } = [];
        public Task WriteTransactionAsync(ServiceAuditTransaction transaction, CancellationToken ct = default)
        {
            Transactions.Add(transaction);
            return Task.CompletedTask;
        }
        public Task UpsertSelectRollupAsync(ServiceSelectAuditRollup rollup, CancellationToken ct = default) => Task.CompletedTask;
    }
}
