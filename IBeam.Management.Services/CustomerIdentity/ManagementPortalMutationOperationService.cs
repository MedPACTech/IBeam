using IBeam.Management.CustomerIdentity;
using IBeam.Management.Services.CustomerIdentity.Delegation;
namespace IBeam.Management.Services.CustomerIdentity;

public sealed class ManagementPortalMutationOperationService(PortalMutationRuntime runtime, PortalMutationReferences references,
    IManagementCustomerIdentityClient client) : IManagementPortalMutationOperationService
{
    public Task<ManagementPortalOperationResult> GetAsync(Guid app, string reference, string authority, CancellationToken ct = default)
        => runtime.Run(this, app, "management.portal.customer-identity.operations.reconcile", async token => {
            if (!ManagementDelegationBinding.Mutating(authority)) throw new ManagementCustomerIdentityInvalidRequestException();
            await runtime.Authorize(app, authority, token);
            var c = await runtime.Resolve(app, authority, token);
            var state = references.Read(c, authority, reference);
            ManagementIdentityOperationDto? result;
            using (var attempt = runtime.Attempt(token)) {
                try {
                    result = state.TargetOperationId is Guid id ? await client.GetOperationAsync(c, id, authority, attempt.Token) :
                        await client.ResolveOperationAsync(c, state.Lookup, state.IdempotencyKey, authority, attempt.Token);
                }
                catch (ManagementCustomerIdentityNotFoundException) { result = null; }
                catch (Exception ex) when (PortalMutationRuntime.Uncertain(ex, token)) { result = null; }
            }
            await runtime.Recheck(c, authority, token);
            await runtime.Outcome(this, app, "management.portal.customer-identity.operations.reconcile." + (result?.Status ?? "unknown"), result?.ResourceId, token);
            return references.Result(c, state, result);
        }, ct);
}
