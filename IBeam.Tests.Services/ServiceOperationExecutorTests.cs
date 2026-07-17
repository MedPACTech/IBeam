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
