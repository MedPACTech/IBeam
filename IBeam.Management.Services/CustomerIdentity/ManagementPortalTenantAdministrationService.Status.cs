using IBeam.Management.CustomerIdentity;
using IBeam.Management.Services.CustomerIdentity.Delegation;
namespace IBeam.Management.Services.CustomerIdentity;
public sealed partial class ManagementPortalTenantAdministrationService
{
    public Task<ManagementPortalTenantMutationResult> SetStatusAsync(Guid app, Guid tenant, bool active, string version, string key, CancellationToken ct = default)
        => runtime.Run(this, app, "management.portal.customer-identity.tenants.status", async token => {
            const string authority = ManagementCustomerIdentityOperations.TenantsStatus;
            await runtime.Authorize(app, authority, token);
            if (tenant == Guid.Empty || string.IsNullOrWhiteSpace(version) || version == "*" || version.Length > 256 || version.Any(char.IsControl) ||
                !ManagementDelegationBinding.ValidIdempotencyKey(key)) throw new ManagementCustomerIdentityInvalidRequestException();
            var connection = await runtime.Resolve(app, authority, token); var state = references.Create(new(authority, tenant, tenant), key);
            ManagementTenantMutationResult? result = null;
            using (var attempt = runtime.Attempt(token)) {
                try { result = await client.SetTenantStatusAsync(connection, tenant, active, version, key, attempt.Token); }
                catch (Exception ex) when (PortalMutationRuntime.Uncertain(ex, token)) { }
            }
            await runtime.Recheck(connection, authority, token); var tracking = references.Result(connection, state, result?.Operation);
            await runtime.Outcome(this, app, "management.portal.customer-identity.tenants.status." + tracking.Status, tenant, token);
            return new ManagementPortalTenantMutationResult(tracking, result?.Tenant, result?.Source, result?.ExternalReference);
        }, ct);
}
