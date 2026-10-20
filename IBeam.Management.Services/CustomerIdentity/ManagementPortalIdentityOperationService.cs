using IBeam.Management.CustomerIdentity;
using IBeam.Management.Services.CustomerIdentity.Delegation;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Services.CustomerIdentity;

public sealed class ManagementPortalIdentityOperationService(IManagementCustomerIdentityConnectionResolver connections,
    IManagementCustomerIdentityClient client, IOptions<ManagementPortalCustomerDirectoryOptions> options,
    IServiceOperationExecutor operations) : IManagementPortalIdentityOperationService
{
    public Task<ManagementIdentityOperationDto> GetAsync(Guid applicationId, Guid operationId, string authority, CancellationToken ct = default)
        => operations.ExecuteAsync(this, async token => {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
            try {
                if (!options.Value.IsValid()) throw new ManagementCustomerIdentityUnavailableException();
                if (operationId == Guid.Empty || !ManagementDelegationBinding.Mutating(authority) && authority != ManagementCustomerIdentityOperations.OnboardingRead)
                    throw new ManagementCustomerIdentityInvalidRequestException();
                var connection = await connections.ResolveAsync(applicationId, authority, timeout.Token);
                var result = await client.GetOperationAsync(connection, operationId, authority, timeout.Token);
                await connections.RecheckAsync(connection, authority, timeout.Token);
                return result;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is ManagementAccessDeniedException or ManagementCustomerIdentityInvalidRequestException or
                ManagementCustomerIdentityNotFoundException or ManagementCustomerIdentityResponseException or ManagementCustomerIdentityRateLimitException or
                ManagementCustomerIdentityUnavailableException) { throw; }
            catch { throw new ManagementCustomerIdentityUnavailableException(); }
        }, new ServiceOperationExecutionOptions { OperationName = "management.portal.customer-identity.operations.read",
            AuditAction = "management.portal.customer-identity.operations.read", EntityId = applicationId, EntityName = "ManagementIdentityOperation",
            PermissionEnabled = false, AuditEnabled = true }, ct);
}
