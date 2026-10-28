using System.Text;
using IBeam.Management;
using IBeam.Management.Operations;
using IBeam.Management.Services;
using IBeam.Management.TenantData;
using IBeam.Management.TenantData.Target.Api;
using IBeam.Management.TenantData.Target.Services;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;

namespace IBeam.Tests.Management.TenantData;

[TestClass]
public sealed class PurgeContractTests
{
    private static ManagementTenantPurgeDefinition Inventory => new(1, "fixture-purge", 1, Array.AsReadOnly(new[] {
        new ManagementTenantPurgeCategory("app-data", "dataset", "delete"), new("audio", "attachment", "delete"),
        new("audit-history", "dataset", "retain"), new("backups", "backup", "exclude"), new("global-accounts", "identity-metadata", "exclude") }));
    [TestMethod]
    public void StrictCompiledContractUsesOnlyIndependentDeleteAndDefaultInstallationIsEmpty()
    {
        var services = new ServiceCollection(); services.AddIBeamManagementServices();
        services.AddIBeamManagementTenantPurgeTargetApi(new ConfigurationBuilder().Build(), _ => { });
        using var provider = services.BuildServiceProvider();
        Assert.IsNull(provider.GetRequiredService<ManagementOperationRegistry>().Find(ManagementTenantDataOperations.Delete));
        var descriptor = ManagementTenantPurgeOperation.Descriptor;
        var registry = new ManagementOperationRegistry([descriptor, ManagementTenantExportOperation.Descriptor]); var codec = new ManagementOperationPayloadCodec(registry);
        Assert.HasCount(1, registry.RequiredFor(ManagementTenantDataOperations.Delete));
        Assert.HasCount(0, registry.Capabilities([ManagementTenantDataOperations.Export], [ManagementTenantDataOperations.Delete], _ => true));
        foreach (var invalid in new[] { descriptor with { RouteTemplate = "/api/management-operations/custom/purge" }, descriptor with { Method = "DELETE" },
            descriptor with { Request = new(typeof(ManagementTenantExportRequest), "management.bad.request") },
            descriptor with { RequiredGrants = [ManagementTenantDataOperations.Delete, ManagementTenantDataOperations.Export] },
            descriptor with { Operation = "management.other.purge", RequiredGrants = ["management.other.purge"] } })
            Assert.Throws<ArgumentException>(() => new ManagementOperationRegistry([invalid]));
        Assert.Throws<ManagementAccessDeniedException>(() => codec.ReadRequest<ManagementTenantPurgeRequest>(ManagementTenantDataOperations.Delete,
            Encoding.UTF8.GetBytes("{\"stage\":\"plan\",\"policyKey\":\"enterprise\",\"expectedVersion\":\"v1\",\"planId\":null,\"confirmationBinding\":null,\"deleteGlobalUser\":true}")));
    }
    [TestMethod]
    public async Task PreflightIsReadOnlyAndRepeatedIntentKeepsItsImmutablePlan()
    {
        var f = new Fixture(); var result = await f.Target.ExecuteAsync(f.Scope, f.Request);
        Assert.AreEqual("planned", result.Outcome); Assert.IsNull(result.Job); Assert.AreEqual(f.Scope.OperationId, result.Plan!.PlanId);
        Assert.AreEqual("exclude", result.Plan.Categories.Single(x => x.Key == "global-accounts").Disposition);
        Assert.AreEqual(result.Plan.ConfirmationBinding, (await f.Target.ExecuteAsync(f.Scope, f.Request)).Plan!.ConfirmationBinding);
        var codec = new ManagementOperationPayloadCodec(new([ManagementTenantPurgeOperation.Descriptor]));
        var wire = Encoding.UTF8.GetString(codec.WriteResult(ManagementTenantDataOperations.Delete, result));
        Assert.DoesNotContain("PRIVATE-", wire); Assert.DoesNotContain("artifact", wire); Assert.DoesNotContain("recipient", wire);
        f.Handler.Verify(x => x.PurgeOrReconcileAsync(It.IsAny<ManagementTenantPurgeExecution>(), It.IsAny<CancellationToken>()), Times.Never);
        f.Handler.Verify(x => x.VerifyAsync(It.IsAny<ManagementTenantPurgeExecution>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    [TestMethod]
    public async Task ConfirmationReservesOneJobAndCannotConsumeThePlanForAnotherIntent()
    {
        var f = new Fixture(); var plan = (await f.Target.ExecuteAsync(f.Scope, f.Request)).Plan!;
        var scope = f.Scope with { OperationId = Guid.NewGuid() }; var request = f.Confirm(plan);
        var result = await f.Target.ExecuteAsync(scope, request);
        Assert.AreEqual("accepted", result.Outcome); Assert.AreEqual("requested", result.Job!.State);
        Assert.AreEqual(scope.OperationId, (await f.Target.ExecuteAsync(scope, request)).Job!.JobId);
        await Assert.ThrowsAsync<ManagementOperationConflictException>(() => f.Target.ExecuteAsync(scope with { OperationId = Guid.NewGuid() }, request));
        var found = await f.Target.ReadReceiptAsync(scope);
        Assert.AreEqual("found", found.Outcome); Assert.AreEqual(scope.OperationId, found.Job!.JobId);
        Assert.AreEqual(1, f.Jobs.Confirmed);
        f.Handler.Verify(x => x.PurgeOrReconcileAsync(It.IsAny<ManagementTenantPurgeExecution>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    [TestMethod]
    [DataRow("org")] [DataRow("app")] [DataRow("realm")] [DataRow("tenant")] [DataRow("actor")]
    [DataRow("application-key")] [DataRow("environment")] [DataRow("version")] [DataRow("inventory")]
    [DataRow("binding")] [DataRow("expired")]
    public async Task PlanCannotBeSubstitutedAcrossScopeVersionInventoryOrExpiry(string change)
    {
        var f = new Fixture(); var plan = (await f.Target.ExecuteAsync(f.Scope, f.Request)).Plan!;
        var execution = f.Execution(f.Scope);
        execution = change switch {
            "org" => execution with { Authority = f.Scope with { OrganizationId = Guid.NewGuid() } },
            "app" => execution with { Authority = f.Scope with { ApplicationId = Guid.NewGuid() } },
            "realm" => execution with { Authority = f.Scope with { RealmId = Guid.NewGuid() } },
            "tenant" => execution with { Authority = f.Scope with { TenantId = Guid.NewGuid() } },
            "actor" => execution with { Authority = f.Scope with { ActorUserId = Guid.NewGuid() } },
            "application-key" => execution with { ApplicationKey = "other" }, "environment" => execution with { Environment = "Production" },
            _ => execution
        };
        plan = change switch { "version" => plan with { ExpectedVersion = "v2" }, "binding" => plan with { ConfirmationBinding = new('A', 64) }, _ => plan };
        var definition = change == "inventory" ? Inventory with { InventoryVersion = 2 } : Inventory;
        Assert.Throws<ManagementOperationPreconditionException>(() => ManagementTenantPurgeProtocol.Plan(plan, execution, definition,
            change == "expired" ? plan.ExpiresUtc : f.Clock.GetUtcNow()));
        Assert.AreEqual(0, f.Jobs.Confirmed);
    }
    [TestMethod]
    [DataRow("stage")] [DataRow("wildcard")] [DataRow("policy")] [DataRow("plan-extra-binding")] [DataRow("missing-plan")] [DataRow("same-job")]
    public async Task InvalidIntentFailsBeforeThePlanOrJobProvider(string change)
    {
        var f = new Fixture(); var request = change switch {
            "stage" => f.Request with { Stage = "delete-everything" }, "wildcard" => f.Request with { ExpectedVersion = "*" },
            "policy" => f.Request with { PolicyKey = "caller-table" }, "plan-extra-binding" => f.Request with { ConfirmationBinding = new('A', 64) },
            "missing-plan" => f.Request with { Stage = "confirm" }, _ => f.Request with { Stage = "confirm", PlanId = f.Scope.OperationId, ConfirmationBinding = new('A', 64) }
        };
        await Assert.ThrowsAsync<ManagementOperationInvalidException>(() => f.Target.ExecuteAsync(f.Scope, request));
        Assert.AreEqual(0, f.Jobs.Created); Assert.AreEqual(0, f.Jobs.Confirmed);
    }
    [TestMethod]
    [DataRow("management.tenants.status")] [DataRow("management.roles.assign")] [DataRow("management.licenses.suspend")] [DataRow("management.tenants.export")]
    public async Task BorrowedPermissionsCannotPlanOrConfirm(string grant)
    {
        var f = new Fixture(); f.Granted = grant;
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Target.ExecuteAsync(f.Scope, f.Request));
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Target.ReadReceiptAsync(f.Scope));
        Assert.AreEqual(0, f.Jobs.Created);
    }
    [TestMethod]
    public async Task RevocationOrNativeRevisionChangeDuringPreviewPreventsConfirmationMaterial()
    {
        foreach (var revoke in new[] { true, false }) {
            var f = new Fixture();
            f.Handler.Setup(x => x.PreviewAsync(It.IsAny<ManagementTenantPurgeExecution>(), It.IsAny<CancellationToken>())).Returns(() => {
                if (revoke) f.Granted = ""; else f.NativeCurrent = false; return Task.CompletedTask;
            });
            if (revoke) await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Target.ExecuteAsync(f.Scope, f.Request));
            else await Assert.ThrowsAsync<ManagementOperationPreconditionException>(() => f.Target.ExecuteAsync(f.Scope, f.Request));
            Assert.AreEqual(0, f.Jobs.Created);
        }
    }
    [TestMethod]
    [DataRow("revoked")] [DataRow("native-version")] [DataRow("inventory")]
    public async Task FreshConfirmationRechecksCurrentAuthorityNativeVersionAndInventory(string change)
    {
        var f = new Fixture(); var plan = (await f.Target.ExecuteAsync(f.Scope, f.Request)).Plan!;
        if (change == "revoked") f.Granted = "";
        if (change == "native-version") f.NativeCurrent = false;
        if (change == "inventory") f.Handler.Setup(x => x.Describe()).Returns(Inventory with { InventoryVersion = 2 });
        if (change == "revoked") await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Target.ExecuteAsync(f.Scope with { OperationId = Guid.NewGuid() }, f.Confirm(plan)));
        else await Assert.ThrowsAsync<ManagementOperationPreconditionException>(() => f.Target.ExecuteAsync(f.Scope with { OperationId = Guid.NewGuid() }, f.Confirm(plan)));
        Assert.AreEqual(0, f.Jobs.Confirmed);
    }
    [TestMethod]
    public async Task ExpiredAndStaleInventoryConfirmationsNeverReserveAJobAndProviderTextIsSuppressed()
    {
        var f = new Fixture(); var plan = (await f.Target.ExecuteAsync(f.Scope, f.Request)).Plan!;
        f.Clock.Now = plan.ExpiresUtc;
        await Assert.ThrowsAsync<ManagementOperationPreconditionException>(() => f.Target.ExecuteAsync(f.Scope with { OperationId = Guid.NewGuid() }, f.Confirm(plan)));
        Assert.AreEqual(0, f.Jobs.Confirmed);
        var other = new Fixture(); other.Handler.Setup(x => x.PreviewAsync(It.IsAny<ManagementTenantPurgeExecution>(), It.IsAny<CancellationToken>())).Throws(new IOException("PRIVATE-PROVIDER-DETAIL"));
        var error = await Assert.ThrowsAsync<ManagementOperationUnavailableException>(() => other.Target.ExecuteAsync(other.Scope, other.Request));
        Assert.DoesNotContain("PRIVATE-", error.ToString());
    }
    [TestMethod]
    [DataRow("handler")] [DataRow("policy")] [DataRow("jobs")] [DataRow("unsupported")] [DataRow("volatile-production")]
    public async Task MissingOrUnsupportedProvidersPreventInstallation(string missing)
    {
        var f = new Fixture(); var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new Host(missing == "volatile-production" ? "Production" : "Testing"));
        services.AddIBeamManagementTenantPurgeTarget(o => { o.Enabled = true; o.PolicyKeys = ["enterprise"]; o.AllowVolatileDevelopmentJobs = true; });
        services.AddSingleton(f.Access.Object);
        if (missing != "handler") services.AddSingleton(f.Handler.Object);
        if (missing != "policy") services.AddSingleton(f.Policy.Object);
        if (missing != "jobs") services.AddSingleton<IManagementTenantPurgeJobService>(f.Jobs);
        if (missing == "unsupported") f.Handler.SetupGet(x => x.ProtocolVersion).Returns(2);
        using var p = services.BuildServiceProvider();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { foreach (var startup in p.GetServices<IHostedService>()) await startup.StartAsync(default); });
    }
    [TestMethod]
    public async Task ExplicitDevelopmentProvidersInstallOnlyTheDeclaredCapability()
    {
        var f = new Fixture(); var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new Host("Testing"));
        services.AddIBeamManagementTenantPurgeTarget(o => { o.Enabled = true; o.PolicyKeys = ["enterprise"]; o.AllowVolatileDevelopmentJobs = true; });
        services.AddSingleton(f.Access.Object); services.AddSingleton(f.Handler.Object); services.AddSingleton(f.Policy.Object);
        services.AddSingleton<IManagementTenantPurgeJobService>(f.Jobs);
        using var provider = services.BuildServiceProvider();
        foreach (var startup in provider.GetServices<IHostedService>()) await startup.StartAsync(default);
        using var scope = provider.CreateScope();
        var registry = provider.GetRequiredService<ManagementOperationRegistry>();
        Assert.HasCount(1, registry.Capabilities([ManagementTenantDataOperations.Delete], [ManagementTenantDataOperations.Delete], x => scope.ServiceProvider.GetService(x) is not null));
        Assert.AreEqual(0, f.Jobs.Created); Assert.AreEqual(0, f.Jobs.Confirmed);
    }
    [TestMethod]
    public void GlobalAccountsMustRemainExplicitlyExcludedAndCannotBeReportedDeleted()
    {
        Assert.Throws<ManagementOperationUnavailableException>(() => ManagementTenantPurgeProtocol.Definition(Inventory with {
            Categories = Inventory.Categories.Where(x => x.Key != "global-accounts").ToArray() }));
        var f = new Fixture(); var e = f.Execution(f.Scope); var now = f.Clock.GetUtcNow();
        var coverage = Inventory.Categories.Select(x => x with { Disposition = x.Disposition switch { "delete" => "deleted", "retain" => "retained", _ => "excluded" } }).ToArray();
        var status = new ManagementTenantPurgeJobStatus(1, e.Authority.TenantId, e.JobId, Guid.NewGuid(), "succeeded", now, now, coverage, null);
        var result = new ManagementTenantPurgeResult(e.Authority.TenantId, e.JobId, "found", null, status);
        Assert.AreEqual("succeeded", ManagementTenantPurgeProtocol.Result(result, e, Inventory).Job!.State);
        coverage[Array.FindIndex(coverage, x => x.Key == "global-accounts")] = new("global-accounts", "identity-metadata", "deleted");
        Assert.Throws<ManagementOperationUnavailableException>(() => ManagementTenantPurgeProtocol.Result(result, e, Inventory));
    }

    private sealed class Fixture
    {
        public Clock Clock { get; } = new();
        public ManagementOperationContext Scope { get; } = CreateScope();
        public ManagementTenantPurgeRequest Request => new("plan", "enterprise", "v1", null, null);
        public string Granted = ManagementTenantDataOperations.Delete;
        public bool NativeCurrent = true;
        public Mock<IManagementOperationAccessService> Access { get; } = new();
        public Mock<IManagementTenantPurgeHandler> Handler { get; } = new(MockBehavior.Strict);
        public Mock<IManagementTenantPurgePolicy> Policy { get; } = new();
        public MemoryJobs Jobs { get; }
        public ManagementTenantPurgeTarget Target { get; }
        public Fixture()
        {
            Access.SetupGet(x => x.ProtocolVersion).Returns(1);
            Access.Setup(x => x.RequireAsync(It.IsAny<string>(), It.IsAny<ManagementOperationContext>(), It.IsAny<CancellationToken>()))
                .Returns((string op, ManagementOperationContext _, CancellationToken _) => Granted == op ? Task.CompletedTask : Task.FromException(new ManagementAccessDeniedException()));
            Handler.SetupGet(x => x.ProtocolVersion).Returns(1); Handler.Setup(x => x.Describe()).Returns(Inventory);
            Handler.Setup(x => x.PreviewAsync(It.IsAny<ManagementTenantPurgeExecution>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Policy.SetupGet(x => x.ProtocolVersion).Returns(1);
            Policy.Setup(x => x.RequireAsync(It.IsAny<ManagementTenantPurgeExecution>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(() => NativeCurrent ? Task.CompletedTask : Task.FromException(new ManagementOperationPreconditionException()));
            Jobs = new(Clock);
            Target = new(Access.Object, Jobs, Handler.Object, Policy.Object,
                Options.Create(new ManagementDiagnosticsOptions { ApplicationKey = "fixture", Environment = "Testing" }),
                Options.Create(new ManagementTenantPurgeOptions { Enabled = true, PolicyKeys = ["enterprise"] }), new ServiceOperationExecutor(), Clock);
        }
        public ManagementTenantPurgeExecution Execution(ManagementOperationContext scope) => new(scope, "fixture", "Testing", scope.OperationId, "enterprise", ManagementTenantPurgeProtocol.Fingerprint(Inventory));
        public ManagementTenantPurgeRequest Confirm(ManagementTenantPurgePlan plan) => new("confirm", plan.PolicyKey, plan.ExpectedVersion, plan.PlanId, plan.ConfirmationBinding);
        private static ManagementOperationContext CreateScope() { var tenant = Guid.NewGuid(); return new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), tenant, Guid.NewGuid(), Guid.NewGuid()) { ResourceId = tenant, ResourceVersion = "v1" }; }
    }
    // Test-only atomic fixture. Production durable storage and execution are the next card.
    private sealed class MemoryJobs(Clock clock) : IManagementTenantPurgeJobService
    {
        public int ProtocolVersion => 1;
        public bool Durable => false;
        public int Created, Confirmed;
        private readonly Dictionary<Guid, ManagementTenantPurgePlan> plans = [];
        private readonly Dictionary<Guid, ManagementTenantPurgeResult> jobs = [];
        private readonly Dictionary<Guid, Guid> consumed = [];
        public Task<ManagementTenantPurgePlan> CreatePlanAsync(ManagementTenantPurgeExecution e, ManagementTenantPurgePlan proposal, CancellationToken ct = default) {
            if (plans.TryGetValue(proposal.PlanId, out var old)) return Task.FromResult(old);
            plans.Add(proposal.PlanId, proposal); Created++; return Task.FromResult(proposal);
        }
        public Task<ManagementTenantPurgePlan?> ReadPlanAsync(ManagementTenantPurgeExecution e, Guid id, CancellationToken ct = default) => Task.FromResult(plans.GetValueOrDefault(id));
        public Task<ManagementTenantPurgeResult> ConfirmAsync(ManagementTenantPurgeExecution e, ManagementTenantPurgePlan plan, CancellationToken ct = default) {
            lock (plans) {
                if (jobs.TryGetValue(e.JobId, out var old)) { if (old.Job!.PlanId != plan.PlanId) throw new ManagementOperationConflictException(); return Task.FromResult(old); }
                if (clock.GetUtcNow() >= plan.ExpiresUtc || consumed.ContainsKey(plan.PlanId)) throw new ManagementOperationConflictException();
                consumed.Add(plan.PlanId, e.JobId); Confirmed++;
                var coverage = plan.Categories.Select(x => x with { Disposition = x.Disposition switch { "delete" => "pending", "retain" => "retained", _ => "excluded" } }).ToArray();
                var now = clock.GetUtcNow(); var result = new ManagementTenantPurgeResult(e.Authority.TenantId, e.JobId, "accepted", null, new(1, e.Authority.TenantId, e.JobId, plan.PlanId, "requested", now, now, coverage, null));
                jobs.Add(e.JobId, result); return Task.FromResult(result);
            }
        }
        public Task<ManagementTenantPurgeResult> FindAsync(ManagementTenantPurgeExecution e, CancellationToken ct = default) => Task.FromResult(
            jobs.TryGetValue(e.JobId, out var result) ? result with { Outcome = "found" } : new(e.Authority.TenantId, e.JobId, "unknown", null, null));
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Host(string environment) : IHostEnvironment {
        public string EnvironmentName { get; set; } = environment; public string ApplicationName { get; set; } = "fixture";
        public string ContentRootPath { get; set; } = "."; public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
