using IBeam.Management.CustomerIdentity;
using IBeam.Management.Services.CustomerIdentity.Delegation;
namespace IBeam.Management.Services.CustomerIdentity;

public sealed partial class ManagementPortalTenantAdministrationService(PortalMutationRuntime runtime, PortalMutationReferences references,
    IManagementCustomerIdentityClient client) : IManagementPortalTenantAdministrationService
{
    public Task<ManagementPortalTenantMutationResult> CreateAsync(Guid app, CreateManagementCustomerTenant request, string idempotencyKey, CancellationToken ct = default)
        => Execute(app, null, request, null, null, idempotencyKey, ct);
    public Task<ManagementPortalTenantMutationResult> RenameAsync(Guid app, Guid tenantId, RenameManagementCustomerTenant request,
        string expectedVersion, string idempotencyKey, CancellationToken ct = default)
        => Execute(app, tenantId, null, request, expectedVersion, idempotencyKey, ct);
    private Task<ManagementPortalTenantMutationResult> Execute(Guid app, Guid? tenantId, CreateManagementCustomerTenant? create,
        RenameManagementCustomerTenant? rename, string? version, string key, CancellationToken ct)
    {
        var operation = tenantId is null ? ManagementCustomerIdentityOperations.TenantsCreate : ManagementCustomerIdentityOperations.TenantsUpdate;
        var action = "management.portal.customer-identity.tenants." + (tenantId is null ? "create" : "update");
        return runtime.Run(this, app, action, async token => {
            await runtime.Authorize(app, operation, token);
            var name = tenantId is null ? create?.Name : rename?.Name;
            if (!ManagementDelegationBinding.ValidIdempotencyKey(key) || tenantId == Guid.Empty || string.IsNullOrWhiteSpace(name) || name.Length > 512 || name.Any(char.IsControl) ||
                tenantId is not null && (string.IsNullOrWhiteSpace(version) || version == "*" || version.Length > 256 || version.Any(char.IsControl)))
                throw new ManagementCustomerIdentityInvalidRequestException();
            var reference = create?.ExternalReference;
            if (reference is not null && (reference.Length > 128 || reference.Any(x => !char.IsAsciiLetterOrDigit(x) && x is not ('-' or '_' or '.' or ':' or '/'))))
                throw new ManagementCustomerIdentityInvalidRequestException();
            if (reference == "") reference = null;
            var c = await runtime.Resolve(app, operation, token);
            var state = references.Create(new(operation, tenantId ?? Guid.Empty, tenantId), key);
            ManagementTenantMutationResult? result;
            using (var attempt = runtime.Attempt(token)) {
                try {
                    result = tenantId is null ? await client.CreateTenantAsync(c, new() { Name = name.Trim(), ExternalReference = reference }, key, attempt.Token) :
                        await client.RenameTenantAsync(c, tenantId.Value, new() { Name = name.Trim() }, version!, key, attempt.Token);
                } catch (Exception ex) when (PortalMutationRuntime.Uncertain(ex, token)) { result = null; }
            }
            await runtime.Recheck(c, operation, token); // Fresh budget: the outbound deadline may already have expired.
            var tracking = references.Result(c, state, result?.Operation);
            await runtime.Outcome(this, app, action + "." + tracking.Status, result?.Operation.ResourceId, token);
            return new ManagementPortalTenantMutationResult(tracking, result?.Tenant, result?.Source, result?.ExternalReference);
        }, ct);
    }
}
