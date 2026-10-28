using IBeam.Management.Operations;
using IBeam.Management.Services.CustomerIdentity.Delegation;
using IBeam.Management.TenantData;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Management.Services;

public sealed class ManagementTenantPurgePortalService(IManagementOperationClient client, IServiceOperationExecutor operations) : IManagementTenantPurgePortalService
{
    public Task<ManagementTenantPurgeResult> PlanAsync(Guid app, Guid tenant, ManagementTenantPurgeRequest request, string key, CancellationToken ct = default)
        => Submit(app, tenant, request, key, "plan", ct);
    public Task<ManagementTenantPurgeResult> ConfirmAsync(Guid app, Guid tenant, ManagementTenantPurgeRequest request, string key, CancellationToken ct = default)
        => Submit(app, tenant, request, key, "confirm", ct);
    private Task<ManagementTenantPurgeResult> Submit(Guid app, Guid tenant, ManagementTenantPurgeRequest request, string key, string stage, CancellationToken ct)
        => Run(app, tenant, stage, async token => {
            if (request is null || request.Stage != stage || !ManagementTenantExportMetadata.Key(request.PolicyKey) || !ManagementTenantPurgeMetadata.Version(request.ExpectedVersion) ||
                !ManagementDelegationBinding.ValidIdempotencyKey(key) || stage == "plan" && (request.PlanId is not null || request.ConfirmationBinding is not null) ||
                stage == "confirm" && (request.PlanId is null || request.PlanId == Guid.Empty || !ManagementTenantPurgeMetadata.Digest(request.ConfirmationBinding)))
                throw new ManagementOperationInvalidException();
            try {
                var result = ManagementTenantPurgeMetadata.Project(await client.ExecuteAsync<ManagementTenantPurgeRequest, ManagementTenantPurgeResult>(app,
                    ManagementTenantDataOperations.Delete, tenant, tenant, request, key, request.ExpectedVersion, token), tenant);
                if (stage == "plan" && (result.Outcome is not ("planned" or "unknown") || result.Plan is { } plan &&
                        (plan.PolicyKey != request.PolicyKey || plan.ExpectedVersion != request.ExpectedVersion)) ||
                    stage == "confirm" && (result.Outcome is not ("accepted" or "unknown") || result.Job is { } job && job.PlanId != request.PlanId))
                    throw new ManagementOperationUnavailableException();
                return result;
            } catch (ManagementOperationUncertainException uncertain) {
                if (uncertain.ApplicationId != app || uncertain.TenantId != tenant || uncertain.ResourceId != tenant || uncertain.OperationId == Guid.Empty)
                    throw new ManagementOperationUnavailableException();
                return new(tenant, uncertain.OperationId, "unknown", null, null);
            }
        }, ct, request?.PlanId);
    public Task<ManagementTenantPurgeResult> FindAsync(Guid app, Guid tenant, Guid job, CancellationToken ct = default)
        => Run(app, tenant, "status", async token => {
            if (job == Guid.Empty) throw new ManagementOperationInvalidException();
            var result = ManagementTenantPurgeMetadata.Project(await client.ReadReceiptAsync<ManagementTenantPurgeResult>(app,
                ManagementTenantDataOperations.Delete, tenant, tenant, job, token), tenant, job);
            if (result.Outcome is not ("found" or "unknown")) throw new ManagementOperationUnavailableException();
            return result;
        }, ct);
    private async Task<ManagementTenantPurgeResult> Run(Guid app, Guid tenant, string stage, Func<CancellationToken, Task<ManagementTenantPurgeResult>> body, CancellationToken ct, Guid? planId = null)
    {
        try { return await operations.ExecuteAsync(this, async token => {
            if (app == Guid.Empty || tenant == Guid.Empty) throw new ManagementOperationInvalidException();
            var result = await body(token);
            // Separate fixed metadata events link plan/job IDs to the same correlation as the app/tenant event.
            if (stage == "confirm" && planId.HasValue) await Audit(app, planId.Value, "confirmation", result.Outcome != "unknown", token);
            var phase = stage == "status" ? result.Job?.State ?? "unknown" : result.Outcome;
            await Audit(app, result.OperationId, phase, phase is not ("unknown" or "failed" or "partially-completed" or "cancelled"), token);
            if (result.Outcome == "unknown") throw new Unknown(result);
            return result;
        }, new ServiceOperationExecutionOptions { OperationName = ManagementTenantDataOperations.Delete,
            AuditAction = stage == "status" ? "management.operations.lookup" : ManagementTenantDataOperations.Delete,
            EntityName = "ManagementTenantPurgePortal", EntityId = app, TenantId = tenant, PermissionEnabled = false, AuditEnabled = true }, ct); }
        catch (Unknown unknown) { return unknown.Result; }
        catch (Exception ex) when (ex is not (ManagementAccessDeniedException or ManagementOperationInvalidException or ManagementOperationConflictException or ManagementOperationPreconditionException or
            ManagementOperationUnavailableException or ManagementAuditUnavailableException) && !(ex is OperationCanceledException && ct.IsCancellationRequested))
            { throw new ManagementOperationUnavailableException(); }
    }
    private async Task Audit(Guid app, Guid reference, string outcome, bool known, CancellationToken ct)
    {
        try { await operations.ExecuteAsync(this, _ => known ? Task.FromResult(true) : Task.FromException<bool>(new UnknownAudit()),
            new ServiceOperationExecutionOptions { OperationName = ManagementTenantDataOperations.Delete, AuditAction = "management.tenants.delete.portal." + outcome,
                EntityName = "ManagementTenantPurgeReference", EntityId = app, TenantId = reference, PermissionEnabled = false, AuditEnabled = true }, ct); }
        catch (UnknownAudit) { }
    }
    private sealed class Unknown(ManagementTenantPurgeResult result) : Exception("The purge outcome is unknown.") { public ManagementTenantPurgeResult Result { get; } = result; }
    private sealed class UnknownAudit : Exception;
}
public static class ManagementTenantPurgePortalExtensions
{
    private sealed class Registration;
    public static IServiceCollection AddIBeamManagementTenantPurgePortal(this IServiceCollection services)
    {
        if (services.Any(x => x.ServiceType == typeof(Registration))) return services;
        services.AddSingleton<Registration>(); services.AddIBeamManagementServices();
        services.AddIBeamManagementOperationContract<ManagementTenantPurgeRequest, ManagementTenantPurgeResult>(ManagementTenantPurgeOperation.Descriptor);
        services.TryAddScoped<IManagementTenantPurgePortalService, ManagementTenantPurgePortalService>(); return services;
    }
}
