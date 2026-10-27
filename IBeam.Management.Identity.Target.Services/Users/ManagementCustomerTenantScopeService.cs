using IBeam.Management.CustomerIdentity;
using IBeam.Services.Abstractions;
namespace IBeam.Management.Identity.Target.Services.Users;

/// <summary>Tenant entity lookup boundary for an already-authorized customer operation; never discloses tenant metadata.</summary>
internal sealed class ManagementCustomerTenantScopeService(IManagementCustomerTenantReadRepository tenants,
    IManagementCustomerIdentityTargetAccessService access, IServiceOperationExecutor operations) : IManagementCustomerTenantScopeService
{
    public string SourceId => tenants.SourceId;
    public Task<ManagementDelegatedActor> RequireAsync(Guid tenantId, string operation, CancellationToken ct = default)
        => operations.ExecuteAsync(this, async token =>
    {
        try
        {
        var actor = await access.RequireAsync(operation, token);
        if (tenantId == Guid.Empty) throw new ManagementCustomerIdentityInvalidRequestException();
        var tenant = await tenants.FindAsync(tenantId, token);
        if (tenant is null || !Guid.TryParseExact(tenant.RowKey, "D", out var id) || id != tenantId) throw new ManagementCustomerIdentityNotFoundException();
        return actor;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is not (ManagementAccessDeniedException or ManagementCustomerIdentityInvalidRequestException or ManagementCustomerIdentityNotFoundException))
        { throw new ManagementCustomerIdentityUnavailableException(); }
    }, new ServiceOperationExecutionOptions { OperationName = "management.identity.tenant-scope.require",
        AuditAction = "management.identity.tenant-scope.require", EntityId = tenantId, EntityName = "ManagementCustomerTenant",
        PermissionEnabled = false, AuditEnabled = true }, ct);
}
