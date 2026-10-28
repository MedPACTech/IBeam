using IBeam.Management.CustomerIdentity;
using IBeam.Management.Operations;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Management.Services;

public sealed class ManagementPortalBulkInvitationRevokeService(IManagementTargetOperationSender sender, IServiceOperationExecutor operations) : IManagementPortalBulkInvitationRevokeService
{
    public Task<ManagementBulkInvitationRevokeResult> ExecuteAsync(Guid app, Guid tenant, Guid batch, ManagementBulkInvitationRevokeRequest request, string key, string version, CancellationToken ct = default)
        => Run(app, tenant, batch, async token => {
            ManagementBulkInvitationRevokeOperation.Validate(request); if (!ManagementBulkInvitationRevokeOperation.Exact(version)) throw new ManagementOperationInvalidException();
            try {
                var value = await sender.ExecuteAsync<ManagementBulkInvitationRevokeRequest, ManagementBulkInvitationRevokeResult>(app, ManagementBulkInvitationRevokeOperation.Name, tenant, batch, request, key, version, token);
                var safe = ManagementBulkInvitationRevokeOperation.Project(value, tenant, batch, value.OperationId);
                if (safe.Outcome == "found") throw new ManagementOperationUnavailableException(); return safe;
            } catch (ManagementOperationUncertainException unknown) {
                if (unknown.ApplicationId != app || unknown.TenantId != tenant || unknown.ResourceId != batch || unknown.OperationId == Guid.Empty) throw new ManagementOperationUnavailableException();
                return new(tenant, batch, unknown.OperationId, "unknown", "unknown", 0, false, []);
            }
        }, ct);
    public Task<ManagementBulkInvitationRevokeResult> ReadReceiptAsync(Guid app, Guid tenant, Guid batch, Guid operation, CancellationToken ct = default)
        => Run(app, tenant, batch, async token => {
            if (operation == Guid.Empty) throw new ManagementOperationInvalidException();
            var value = ManagementBulkInvitationRevokeOperation.Project(await sender.ReadReceiptAsync<ManagementBulkInvitationRevokeResult>(app, ManagementBulkInvitationRevokeOperation.Name, tenant, batch, operation, token), tenant, batch, operation);
            if (value.Outcome == "accepted") throw new ManagementOperationUnavailableException(); return value;
        }, ct);
    private Task<ManagementBulkInvitationRevokeResult> Run(Guid app, Guid tenant, Guid batch, Func<CancellationToken, Task<ManagementBulkInvitationRevokeResult>> body, CancellationToken ct)
        => operations.ExecuteAsync(this, async token => {
            if (app == Guid.Empty || tenant == Guid.Empty || batch == Guid.Empty) throw new ManagementOperationInvalidException(); return await body(token);
        }, new ServiceOperationExecutionOptions { OperationName = ManagementBulkInvitationRevokeOperation.Name, AuditAction = ManagementBulkInvitationRevokeOperation.Name,
            EntityName = "ManagementPortalBulkInvitationBatch", EntityId = app, TenantId = tenant, PermissionEnabled = false, AuditEnabled = true }, ct);
}
public static class ManagementPortalBulkInvitationRevokeExtensions
{
    private sealed class Registration;
    public static IServiceCollection AddIBeamManagementPortalBulkInvitationRevoke(this IServiceCollection services)
    {
        if (services.Any(x => x.ServiceType == typeof(Registration))) return services;
        services.AddSingleton<Registration>(); services.AddIBeamManagementServices();
        services.AddIBeamManagementOperationContract<ManagementBulkInvitationRevokeRequest, ManagementBulkInvitationRevokeResult>(ManagementBulkInvitationRevokeOperation.Descriptor);
        services.TryAddScoped<IManagementPortalBulkInvitationRevokeService, ManagementPortalBulkInvitationRevokeService>(); return services;
    }
}
