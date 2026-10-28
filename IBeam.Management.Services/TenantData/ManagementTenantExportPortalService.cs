using IBeam.Management.Operations;
using IBeam.Management.Services.CustomerIdentity.Delegation;
using IBeam.Management.TenantData;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Management.Services;

public sealed class ManagementTenantExportPortalService(IManagementOperationClient client, IServiceOperationExecutor operations) : IManagementTenantExportPortalService
{
    public Task<ManagementTenantExportResult> SubmitAsync(Guid app, Guid tenant, ManagementTenantExportRequest request, string key, CancellationToken ct = default)
        => Run(app, tenant, async token =>
        {
            if (request is null || !ManagementTenantExportMetadata.Key(request.PolicyKey) || request.ExpectedVersion is not { Length: > 0 and <= 256 } ||
                request.ExpectedVersion.Trim() != request.ExpectedVersion || request.ExpectedVersion == "*" || request.ExpectedVersion.Any(char.IsControl) || !ManagementDelegationBinding.ValidIdempotencyKey(key))
                throw new ManagementOperationInvalidException();
            try
            {
                var result = ManagementTenantExportMetadata.Project(await client.ExecuteAsync<ManagementTenantExportRequest, ManagementTenantExportResult>(app,
                    ManagementTenantDataOperations.Export, tenant, tenant, request, key, request.ExpectedVersion, token), tenant);
                if (result.Outcome == "found") throw new ManagementOperationUnavailableException();
                return result;
            }
            catch (ManagementOperationUncertainException uncertain)
            {
                if (uncertain.ApplicationId != app || uncertain.TenantId != tenant || uncertain.ResourceId != tenant || uncertain.OperationId == Guid.Empty)
                    throw new ManagementOperationUnavailableException();
                return new(tenant, uncertain.OperationId, "unknown", null);
            }
        }, ct);
    public Task<ManagementTenantExportResult> FindAsync(Guid app, Guid tenant, Guid job, CancellationToken ct = default)
        => Run(app, tenant, async token =>
        {
            if (job == Guid.Empty) throw new ManagementOperationInvalidException();
            var result = ManagementTenantExportMetadata.Project(await client.ReadReceiptAsync<ManagementTenantExportResult>(app, ManagementTenantDataOperations.Export, tenant, tenant, job, token), tenant, job);
            if (result.Outcome == "accepted") throw new ManagementOperationUnavailableException();
            return result;
        }, ct, lookup: true);
    private async Task<ManagementTenantExportResult> Run(Guid app, Guid tenant, Func<CancellationToken, Task<ManagementTenantExportResult>> body, CancellationToken ct, bool lookup = false)
    {
        try { return await operations.ExecuteAsync(this, async token =>
        {
            if (app == Guid.Empty || tenant == Guid.Empty) throw new ManagementOperationInvalidException();
            var result = await body(token);
            if (result.Outcome == "unknown") throw new Unknown(result);
            return result;
        }, new ServiceOperationExecutionOptions { OperationName = ManagementTenantDataOperations.Export, AuditAction = lookup ? "management.operations.lookup" : ManagementTenantDataOperations.Export,
            EntityName = "ManagementTenantExportPortal", EntityId = app, TenantId = tenant, PermissionEnabled = false, AuditEnabled = true }, ct); }
        catch (Unknown unknown) { return unknown.Result; }
        catch (Exception ex) when (ex is not (ManagementAccessDeniedException or ManagementOperationInvalidException or ManagementOperationConflictException or ManagementOperationPreconditionException or
            ManagementOperationUnavailableException or ManagementAuditUnavailableException) && !(ex is OperationCanceledException && ct.IsCancellationRequested))
        { throw new ManagementOperationUnavailableException(); }
    }
    private sealed class Unknown(ManagementTenantExportResult result) : Exception("The export outcome is unknown.")
    { public ManagementTenantExportResult Result { get; } = result; }
}
public static class ManagementTenantExportPortalExtensions
{
    private sealed class Registration;
    public static IServiceCollection AddIBeamManagementTenantExportPortal(this IServiceCollection services)
    {
        if (services.Any(x => x.ServiceType == typeof(Registration))) return services;
        services.AddSingleton<Registration>(); services.AddIBeamManagementServices();
        services.AddIBeamManagementOperationContract<ManagementTenantExportRequest, ManagementTenantExportResult>(ManagementTenantExportOperation.Descriptor);
        services.TryAddScoped<IManagementTenantExportPortalService, ManagementTenantExportPortalService>(); return services;
    }
}
