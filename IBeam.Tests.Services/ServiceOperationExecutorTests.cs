using IBeam.AccessControl;
using IBeam.Repositories.Abstractions;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Claims;

namespace IBeam.Tests.Services;

[TestClass]
public sealed class ServiceOperationExecutorTests
{
    [TestMethod]
    public async Task ExecuteAsync_WithAttributedCustomMethod_AuthorizesAndAudits()
    {
        var tenantId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("role", "Clinician")], "test"));
        var sink = new Mock<IAuditTrailSink>(MockBehavior.Strict);
        var authorizer = new Mock<IServiceOperationAuthorizer>(MockBehavior.Strict);
        var executor = new ServiceOperationExecutor(
            auditTrailSink: sink.Object,
            serviceOperationAuthorizer: authorizer.Object,
            serviceOperationPrincipalProvider: new FixedPrincipalProvider(principal),
            auditOptionsMonitor: OptionsMonitor(new ServiceAuditOptions { Enabled = true }),
            tenantContext: new FixedTenantContext(tenantId));
        var service = new PatientWorkflowService(executor);

        authorizer
            .Setup(x => x.AuthorizeAsync(
                It.Is<ServiceOperationAuthorizationRequest>(r =>
                    r.TenantId == tenantId &&
                    ReferenceEquals(r.Principal, principal) &&
                    r.OperationName == "patients.discharge"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceOperationAuthorizationResult.Allow("patients.discharge", "test"));

        sink
            .Setup(x => x.WriteTransactionAsync(
                It.Is<ServiceAuditTransaction>(t =>
                    t.ServiceName == nameof(PatientWorkflowService) &&
                    t.EntityName == "patients" &&
                    t.Operation == ServiceAuditOperation.Custom &&
                    t.Action == "patients.discharge" &&
                    t.EntityId == patientId &&
                    t.TenantId == tenantId &&
                    t.Succeeded &&
                    t.ErrorType == null &&
                    t.DurationMs >= 0 &&
                    t.TransformedJson != null),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await service.DischargeAsync(patientId);

        Assert.IsTrue(service.Called);
        authorizer.VerifyAll();
        sink.VerifyAll();
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenCustomMethodFails_WritesFailureAudit()
    {
        var patientId = Guid.NewGuid();
        var sink = new Mock<IAuditTrailSink>(MockBehavior.Strict);
        var executor = new ServiceOperationExecutor(
            auditTrailSink: sink.Object,
            auditOptionsMonitor: OptionsMonitor(new ServiceAuditOptions { Enabled = true }));
        var service = new PatientWorkflowService(executor);

        sink
            .Setup(x => x.WriteTransactionAsync(
                It.Is<ServiceAuditTransaction>(t =>
                    t.ServiceName == nameof(PatientWorkflowService) &&
                    t.EntityName == "patients" &&
                    t.Action == "patients.fail" &&
                    t.EntityId == patientId &&
                    !t.Succeeded &&
                    t.ErrorType == typeof(InvalidOperationException).FullName &&
                    t.ErrorMessage == "No bed assigned." &&
                    t.DurationMs >= 0),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var ex = await AssertThrowsAsync<InvalidOperationException>(() => service.FailDischargeAsync(patientId));

        Assert.AreEqual("No bed assigned.", ex.Message);
        sink.VerifyAll();
    }

    private static async Task<TException> AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
            Assert.Fail($"Expected exception {typeof(TException).Name} was not thrown.");
            throw new InvalidOperationException("Unreachable");
        }
        catch (TException ex)
        {
            return ex;
        }
    }

    private static IOptionsMonitor<ServiceAuditOptions> OptionsMonitor(ServiceAuditOptions options)
    {
        var monitor = new Mock<IOptionsMonitor<ServiceAuditOptions>>();
        monitor.SetupGet(x => x.CurrentValue).Returns(options);
        return monitor.Object;
    }

    [IBeamOperation("patients")]
    private sealed class PatientWorkflowService
    {
        private readonly IServiceOperationExecutor _operations;

        public PatientWorkflowService(IServiceOperationExecutor operations)
        {
            _operations = operations;
        }

        public bool Called { get; private set; }

        [IBeamOperation("patients.discharge")]
        public Task DischargeAsync(Guid patientId, CancellationToken ct = default)
            => _operations.ExecuteAsync(
                this,
                _ =>
                {
                    Called = true;
                    return Task.CompletedTask;
                },
                new ServiceOperationExecutionOptions
                {
                    EntityId = patientId,
                    TransformedData = new PatientOperationState(patientId, "discharged")
                },
                ct);

        [IBeamOperation("patients.fail")]
        public Task FailDischargeAsync(Guid patientId, CancellationToken ct = default)
            => _operations.ExecuteAsync(
                this,
                _ => Task.FromException(new InvalidOperationException("No bed assigned.")),
                new ServiceOperationExecutionOptions { EntityId = patientId },
                ct);
    }

    [TestMethod]
    public async Task ExecuteAsync_InsideASystemScope_SkipsAuthorizationWhenThereIsNoTenant()
    {
        // A verified machine callback has no principal and no tenant. Without the scope this is
        // exactly the "tenantId is required for service operation authorization" failure that made
        // every provider webhook 500 in a host that registers an authorizer.
        var authorizer = new Mock<IServiceOperationAuthorizer>(MockBehavior.Strict);
        var systemContext = new ServiceOperationSystemContext();
        var executor = new ServiceOperationExecutor(
            serviceOperationAuthorizer: authorizer.Object,
            tenantContext: new FixedTenantContext(Guid.Empty),
            systemContext: systemContext);
        var service = new PatientWorkflowService(executor);

        using (systemContext.Enter("billing.provider-webhook"))
        {
            await service.DischargeAsync(Guid.NewGuid());
        }

        // Strict mock: the authorizer being consulted at all would fail the test.
        authorizer.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task ExecuteAsync_WithNoTenantAndNoSystemScope_StillRefuses()
    {
        // The exemption must be the scope, not the absence of a tenant. If this ever passes, an
        // unauthenticated caller with no tenant would sail through every service operation.
        var authorizer = new Mock<IServiceOperationAuthorizer>(MockBehavior.Strict);
        var executor = new ServiceOperationExecutor(
            serviceOperationAuthorizer: authorizer.Object,
            tenantContext: new FixedTenantContext(Guid.Empty),
            systemContext: new ServiceOperationSystemContext());
        var service = new PatientWorkflowService(executor);

        await Assert.ThrowsExactlyAsync<AccessControlException>(() => service.DischargeAsync(Guid.NewGuid()));
    }

    [TestMethod]
    public async Task ExecuteAsync_AfterASystemScopeCloses_AuthorizesAgain()
    {
        // The exemption must not outlive the callback that opened it.
        var authorizer = new Mock<IServiceOperationAuthorizer>(MockBehavior.Strict);
        var systemContext = new ServiceOperationSystemContext();
        var executor = new ServiceOperationExecutor(
            serviceOperationAuthorizer: authorizer.Object,
            tenantContext: new FixedTenantContext(Guid.Empty),
            systemContext: systemContext);
        var service = new PatientWorkflowService(executor);

        using (systemContext.Enter("billing.provider-webhook"))
        {
            await service.DischargeAsync(Guid.NewGuid());
        }

        Assert.IsFalse(systemContext.IsActive);
        await Assert.ThrowsExactlyAsync<AccessControlException>(() => service.DischargeAsync(Guid.NewGuid()));
    }

    [TestMethod]
    public void SystemScope_NestsAndSurvivesADoubleDispose()
    {
        // A double dispose must not pop an outer scope and silently exempt work meant to be checked.
        var systemContext = new ServiceOperationSystemContext();

        var outer = systemContext.Enter("outer");
        var inner = systemContext.Enter("inner");
        Assert.AreEqual("inner", systemContext.Reason);

        inner.Dispose();
        inner.Dispose();
        Assert.IsTrue(systemContext.IsActive);
        Assert.AreEqual("outer", systemContext.Reason);

        outer.Dispose();
        Assert.IsFalse(systemContext.IsActive);
    }

    [TestMethod]
    public void SystemScope_RequiresAReason()
    {
        var systemContext = new ServiceOperationSystemContext();

        Assert.ThrowsExactly<ArgumentException>(() => systemContext.Enter("  "));
    }

    private sealed record PatientOperationState(Guid PatientId, string Status);

    private sealed class FixedPrincipalProvider : IServiceOperationPrincipalProvider
    {
        private readonly ClaimsPrincipal _principal;

        public FixedPrincipalProvider(ClaimsPrincipal principal)
        {
            _principal = principal;
        }

        public ClaimsPrincipal? GetPrincipal() => _principal;
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        public FixedTenantContext(Guid tenantId)
        {
            TenantId = tenantId;
        }

        public Guid? TenantId { get; }

        public bool IsTenantIdSet() => TenantId.HasValue && TenantId.Value != Guid.Empty;
    }
}
