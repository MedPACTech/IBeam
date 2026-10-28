using IBeam.Management.Operations;
using IBeam.Management.Services;
using IBeam.Management.TenantData;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IBeam.Management.TenantData.Target.Services;

public sealed class ManagementTenantPurgeOptions
{
    public bool Enabled { get; set; }
    public bool AllowVolatileDevelopmentJobs { get; set; }
    public int PlanLifetimeSeconds { get; set; } = 180;
    public string[] PolicyKeys { get; set; } = [];
}
public sealed class ManagementTenantPurgeTarget(IManagementOperationAccessService access, IManagementTenantPurgeJobService jobs,
    IManagementTenantPurgeHandler handler, IManagementTenantPurgePolicy policy, IOptions<ManagementDiagnosticsOptions> deployment,
    IOptions<ManagementTenantPurgeOptions> options, IServiceOperationExecutor operations, TimeProvider clock)
    : IManagementOperationHandler<ManagementTenantPurgeRequest, ManagementTenantPurgeResult>, IManagementOperationReceiptHandler<ManagementTenantPurgeResult>
{
    public Task<ManagementTenantPurgeResult> ExecuteAsync(ManagementOperationContext context, ManagementTenantPurgeRequest request, CancellationToken ct = default)
        => Run(context, request?.Stage == "plan", async token =>
        {
            if (request is null || request.Stage is not ("plan" or "confirm") || !options.Value.PolicyKeys.Contains(request.PolicyKey, StringComparer.Ordinal) ||
                request.ExpectedVersion is not { Length: > 0 and <= 256 } || request.ExpectedVersion.Trim() != request.ExpectedVersion || request.ExpectedVersion == "*" ||
                request.ExpectedVersion.Any(char.IsControl) || context.ResourceVersion != request.ExpectedVersion ||
                request.Stage == "plan" && (request.PlanId is not null || request.ConfirmationBinding is not null) ||
                request.Stage == "confirm" && (request.PlanId is null || request.PlanId == Guid.Empty || request.PlanId == context.OperationId || request.ConfirmationBinding is not { Length: 64 }))
                throw new ManagementOperationInvalidException();
            var definition = ManagementTenantPurgeProtocol.Definition(handler.Describe());
            var execution = Execution(context, request.PolicyKey, definition);
            await policy.RequireAsync(execution, request.ExpectedVersion, token);
            if (request.Stage == "plan")
            {
                await handler.PreviewAsync(execution, token); // Read-only: no purge, fence or account-cleanup hook runs here.
                await access.RequireAsync(ManagementTenantDataOperations.Delete, context, token);
                await policy.RequireAsync(execution, request.ExpectedVersion, token);
                var now = clock.GetUtcNow();
                var plan = new ManagementTenantPurgePlan(1, context.TenantId, context.OperationId, request.PolicyKey, request.ExpectedVersion,
                    execution.InventoryFingerprint, "", now, now.AddSeconds(options.Value.PlanLifetimeSeconds), definition.Categories.ToArray());
                plan = plan with { ConfirmationBinding = ManagementTenantPurgeProtocol.Binding(execution, plan) };
                var stored = ManagementTenantPurgeProtocol.Plan(await jobs.CreatePlanAsync(execution, plan, token), execution, definition, clock.GetUtcNow());
                if (stored.PlanId != context.OperationId || stored.ExpectedVersion != request.ExpectedVersion) throw new ManagementOperationConflictException();
                return new(context.TenantId, context.OperationId, "planned", stored, null);
            }
            var found = await jobs.ReadPlanAsync(execution, request.PlanId!.Value, token) ?? throw new ManagementOperationPreconditionException();
            var verified = ManagementTenantPurgeProtocol.Plan(found, execution, definition, clock.GetUtcNow());
            if (verified.PlanId != request.PlanId || verified.ExpectedVersion != request.ExpectedVersion || verified.ConfirmationBinding != request.ConfirmationBinding)
                throw new ManagementOperationPreconditionException();
            // The durable provider must atomically consume the plan and reserve this one job, after its own revision/fence check.
            await access.RequireAsync(ManagementTenantDataOperations.Delete, context, token);
            await policy.RequireAsync(execution, request.ExpectedVersion, token);
            var accepted = ManagementTenantPurgeProtocol.Result(await jobs.ConfirmAsync(execution, verified, token), execution, definition);
            if (accepted.Outcome is not ("accepted" or "unknown") || accepted.Job is { } job && job.PlanId != verified.PlanId) throw new ManagementOperationUnavailableException();
            return accepted;
        }, ct);

    public Task<ManagementTenantPurgeResult> ReadReceiptAsync(ManagementOperationContext context, CancellationToken ct = default)
        => Run(context, true, async token => {
            var definition = ManagementTenantPurgeProtocol.Definition(handler.Describe());
            var execution = Execution(context, "", definition);
            await policy.RequireStatusAsync(execution, token);
            var result = ManagementTenantPurgeProtocol.Result(await jobs.FindAsync(execution, token), execution, definition);
            if (result.Outcome is not ("found" or "unknown")) throw new ManagementOperationUnavailableException();
            return result;
        }, ct);
    private ManagementTenantPurgeExecution Execution(ManagementOperationContext context, string policyKey, ManagementTenantPurgeDefinition definition)
        => new(context, deployment.Value.ApplicationKey, deployment.Value.Environment, context.OperationId, policyKey, ManagementTenantPurgeProtocol.Fingerprint(definition));
    private async Task<ManagementTenantPurgeResult> Run(ManagementOperationContext scope, bool readOnly,
        Func<CancellationToken, Task<ManagementTenantPurgeResult>> action, CancellationToken ct)
    {
        try { return await operations.ExecuteAsync(this, async token => {
            try {
                if (access.ProtocolVersion != 1 || scope.OrganizationId == Guid.Empty || scope.ApplicationId == Guid.Empty || scope.RealmId == Guid.Empty ||
                    scope.TenantId == Guid.Empty || scope.ActorUserId == Guid.Empty || scope.OperationId == Guid.Empty || scope.ResourceId != scope.TenantId)
                    throw new ManagementAccessDeniedException();
                await access.RequireAsync(ManagementTenantDataOperations.Delete, scope, token);
                var result = await action(token);
                await access.RequireAsync(ManagementTenantDataOperations.Delete, scope, token);
                if (result.Outcome == "unknown") throw new Unknown(result);
                return result;
            } catch (Exception ex) when (ex is not (ManagementAccessDeniedException or ManagementOperationInvalidException or ManagementOperationPreconditionException or
                ManagementOperationConflictException or ManagementOperationUnavailableException or Unknown) && !(ex is OperationCanceledException && token.IsCancellationRequested))
                { throw new ManagementOperationUnavailableException(); }
        }, new ServiceOperationExecutionOptions { OperationName = readOnly ? "management.operations.lookup" : ManagementTenantDataOperations.Delete,
            AuditAction = readOnly ? "management.operations.lookup" : ManagementTenantDataOperations.Delete, EntityName = readOnly ? "ManagementTenantPurgePlanOrStatus" : "ManagementTenantPurgeJob",
            EntityId = scope.TenantId, PermissionEnabled = false, AuditEnabled = true }, ct); }
        catch (Unknown unknown) { return unknown.Result; }
    }
    private sealed class Unknown(ManagementTenantPurgeResult result) : Exception("The purge outcome is unknown.") { public ManagementTenantPurgeResult Result { get; } = result; }
}

public static class ManagementTenantPurgeServiceExtensions
{
    public static IServiceCollection AddIBeamManagementTenantPurgeTarget(this IServiceCollection services, Action<ManagementTenantPurgeOptions> configure)
    {
        var settings = new ManagementTenantPurgeOptions(); configure(settings);
        if (!settings.Enabled) return services;
        if (services.Any(x => x.ServiceType == typeof(Registration))) throw new InvalidOperationException("Tenant purge is already registered.");
        services.AddSingleton<Registration>(); services.AddIBeamManagementServices(); services.TryAddSingleton(TimeProvider.System);
        var keys = settings.PolicyKeys?.ToArray();
        services.AddOptions<ManagementTenantPurgeOptions>().Configure(o => { o.Enabled = true; o.AllowVolatileDevelopmentJobs = settings.AllowVolatileDevelopmentJobs;
            o.PlanLifetimeSeconds = settings.PlanLifetimeSeconds; o.PolicyKeys = keys ?? []; })
            .Validate(o => o.PlanLifetimeSeconds is >= 30 and <= 300 && o.PolicyKeys is { Length: > 0 and <= 32 } && o.PolicyKeys.All(ManagementTenantExportMetadata.Key) &&
                o.PolicyKeys.Distinct(StringComparer.Ordinal).Count() == o.PolicyKeys.Length, "Explicit bounded purge policy keys and a 30-300 second confirmation lifetime are required.").ValidateOnStart();
        services.AddIBeamManagementOperation<ManagementTenantPurgeRequest, ManagementTenantPurgeResult, ManagementTenantPurgeTarget>(ManagementTenantPurgeOperation.Descriptor);
        services.AddHostedService<Validate>(); return services;
    }
    private sealed class Registration;
    private sealed class Validate(IServiceScopeFactory scopes, IOptions<ManagementTenantPurgeOptions> options, IHostEnvironment host) : IHostedService
    {
        public Task StartAsync(CancellationToken ct)
        {
            using var scope = scopes.CreateScope(); var p = scope.ServiceProvider;
            var handler = p.GetRequiredService<IManagementTenantPurgeHandler>();
            _ = ManagementTenantPurgeProtocol.Definition(handler.Describe());
            var jobs = p.GetRequiredService<IManagementTenantPurgeJobService>();
            if (handler.ProtocolVersion != 1 || jobs.ProtocolVersion != 1 || p.GetRequiredService<IManagementTenantPurgePolicy>().ProtocolVersion != 1 ||
                p.GetRequiredService<IManagementOperationAccessService>().ProtocolVersion != 1 ||
                !jobs.Durable && !(options.Value.AllowVolatileDevelopmentJobs && host.EnvironmentName is "Development" or "Testing"))
                throw new InvalidOperationException("Purge requires compatible native inventory/policy, live access and atomic durable plan/job providers.");
            _ = p.GetRequiredService<IManagementOperationHandler<ManagementTenantPurgeRequest, ManagementTenantPurgeResult>>();
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
