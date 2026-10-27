using IBeam.Management.CustomerIdentity;
using IBeam.Management.Services.CustomerIdentity.Delegation;
namespace IBeam.Management.Services.CustomerIdentity;

public sealed class ManagementPortalCustomerOwnershipService(PortalMutationRuntime runtime, PortalMutationReferences references,
    IManagementCustomerIdentityClient client) : IManagementPortalCustomerOwnershipService
{
    public Task<ManagementCustomerOwnershipDto> ReadAsync(Guid app, Guid tenant, CancellationToken ct = default)
        => runtime.Run(this, app, "management.portal.customer-identity.owners.read", async token => {
            if (tenant == Guid.Empty) throw new ManagementCustomerIdentityInvalidRequestException();
            await runtime.Authorize(app, ManagementCustomerIdentityOperations.OwnersRead, token);
            var connection = await runtime.Resolve(app, ManagementCustomerIdentityOperations.OwnersRead, token);
            ManagementCustomerOwnershipDto value;
            using (var attempt = runtime.Attempt(token)) value = await client.ReadOwnershipAsync(connection, tenant, attempt.Token);
            await runtime.Recheck(connection, ManagementCustomerIdentityOperations.OwnersRead, token);
            return value;
        }, ct);
    public Task<ManagementPortalCustomerOwnershipResult> TransferAsync(Guid app, Guid tenant, TransferManagementCustomerOwnership request, string version, string key, CancellationToken ct = default)
        => runtime.Run(this, app, "management.portal.customer-identity.owners.transfer", async token => {
            await runtime.Authorize(app, ManagementCustomerIdentityOperations.OwnersTransfer, token);
            if (tenant == Guid.Empty || request is null || !request.IsValid() || !TransferManagementCustomerOwnership.Exact(version) || !ManagementDelegationBinding.ValidIdempotencyKey(key))
                throw new ManagementCustomerIdentityInvalidRequestException();
            var connection = await runtime.Resolve(app, ManagementCustomerIdentityOperations.OwnersTransfer, token);
            var state = references.Create(new(ManagementCustomerIdentityOperations.OwnersTransfer, tenant, tenant), key);
            ManagementCustomerOwnershipResult? result = null;
            using (var attempt = runtime.Attempt(token)) {
                try { result = await client.TransferOwnershipAsync(connection, tenant, request, version, key, attempt.Token); }
                catch (Exception ex) when (PortalMutationRuntime.Uncertain(ex, token)) { }
            }
            await runtime.Recheck(connection, ManagementCustomerIdentityOperations.OwnersTransfer, token);
            var tracking = references.Result(connection, state, result?.Operation);
            await runtime.Outcome(this, app, "management.portal.customer-identity.owners.transfer." + tracking.Status, tenant, token);
            return new ManagementPortalCustomerOwnershipResult(tracking);
        }, ct);
}
