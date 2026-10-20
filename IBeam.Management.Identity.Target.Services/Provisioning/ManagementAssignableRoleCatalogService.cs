using IBeam.Identity.Interfaces;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Identity.Target.Services.Operations;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Identity.Target.Services.Provisioning;

public sealed class ManagementAssignableRoleCatalogService(IManagementAssignableRoleReadRepository repository, IIdentityTenantStore tenants,
    IManagementCustomerIdentityTargetAccessService access, IOptions<ManagementCustomerProvisioningOptions> options,
    IOptions<ManagementIdentityOperationOptions> runtime, IServiceOperationExecutor operations) : IManagementAssignableRoleCatalogService
{
    public Task<IReadOnlyList<ManagementAssignableRoleDto>> ListAsync(Guid tenantId, CancellationToken ct = default)
        => Run(tenantId, ManagementCustomerIdentityOperations.RolesRead, async token => await Read(tenantId, token), ct);
    public Task<IReadOnlyList<ManagementAssignableRoleDto>> RequireAsync(Guid tenantId, string policyKey, IReadOnlyList<Guid> roleIds, string authority, CancellationToken ct = default)
        => Run(tenantId, authority, async token => {
            if (authority is not (ManagementCustomerIdentityOperations.UsersProvision or ManagementCustomerIdentityOperations.RolesAssign)) throw new ManagementAccessDeniedException();
            if (policyKey is null || !options.Value.Policies.ContainsKey(policyKey) || roleIds is null || roleIds.Count > 8 ||
                authority == ManagementCustomerIdentityOperations.UsersProvision && roleIds.Count == 0 ||
                roleIds.Any(x => x == Guid.Empty) || roleIds.Distinct().Count() != roleIds.Count) throw new ManagementCustomerIdentityInvalidRequestException();
            var tenant = await tenants.FindByIdAsync(tenantId, token) ?? throw new ManagementCustomerIdentityNotFoundException();
            if (!tenant.IsActive) throw new ManagementIdentityOperationConflictException();
            var roles = (await Read(tenantId, token)).Where(x => roleIds.Contains(x.RoleId) && x.PolicyKeys.Contains(policyKey, StringComparer.Ordinal)).ToArray();
            if (roles.Length != roleIds.Count) throw new ManagementAccessDeniedException();
            return roles;
        }, ct);
    private async Task<IReadOnlyList<ManagementAssignableRoleDto>> Read(Guid tenantId, CancellationToken ct)
    {
        var names = options.Value.Policies.Values.SelectMany(x => x).Distinct(StringComparer.Ordinal).ToArray();
        var rows = await repository.ReadAsync(tenantId, names, ct);
        if (rows.Count > 32 || rows.Any(x => x.TenantId != tenantId || x.RoleId == Guid.Empty || !names.Contains(x.Name, StringComparer.Ordinal)) ||
            rows.GroupBy(x => x.RoleId).Any(x => x.Count() != 1) || rows.Where(x => x.IsActive).GroupBy(x => x.Name, StringComparer.Ordinal).Any(x => x.Count() != 1))
            throw new ManagementCustomerIdentityUnavailableException();
        return rows.Where(x => x.IsActive).OrderBy(x => x.Name, StringComparer.Ordinal).Select(x => new ManagementAssignableRoleDto(tenantId, x.RoleId, x.Name,
            options.Value.Policies.Where(p => p.Value.Contains(x.Name, StringComparer.Ordinal)).Select(p => p.Key).Order(StringComparer.Ordinal).ToArray())).ToArray();
    }
    private Task<IReadOnlyList<ManagementAssignableRoleDto>> Run(Guid tenantId, string operation, Func<CancellationToken, Task<IReadOnlyList<ManagementAssignableRoleDto>>> run, CancellationToken ct)
        => operations.ExecuteAsync(this, async token => {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(runtime.Value.TimeoutSeconds));
            try {
                await access.RequireAsync(operation, timeout.Token);
                if (!options.Value.IsValid() || tenantId == Guid.Empty) throw new ManagementCustomerIdentityInvalidRequestException();
                var tenant = await tenants.FindByIdAsync(tenantId, timeout.Token);
                if (tenant?.TenantId != tenantId) throw new ManagementCustomerIdentityNotFoundException();
                var result = await run(timeout.Token); await access.RequireAsync(operation, timeout.Token); return result;
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is ManagementAccessDeniedException or ManagementCustomerIdentityInvalidRequestException or ManagementCustomerIdentityNotFoundException or ManagementIdentityOperationConflictException) { throw; }
            catch { throw new ManagementCustomerIdentityUnavailableException(); }
        }, new ServiceOperationExecutionOptions { OperationName = "management.identity.roles.catalog", AuditAction = "management.identity.roles.catalog", EntityId = tenantId,
            PermissionEnabled = false, AuditEnabled = true }, ct);
}
