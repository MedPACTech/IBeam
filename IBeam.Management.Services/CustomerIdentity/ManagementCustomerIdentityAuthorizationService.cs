using IBeam.Management.Operations;
using IBeam.Management.CustomerIdentity;
using IBeam.Services.Abstractions;
namespace IBeam.Management.Services.CustomerIdentity;

/// <summary>Portal authorization boundary; it never resolves machine secrets or creates a customer principal.</summary>
public sealed class ManagementCustomerIdentityAuthorizationService(IManagementApplicationAccessService access,
    IManagementCustomerIdentityAvailabilityService availability, IServiceOperationExecutor operations)
    : IManagementCustomerIdentityAuthorizationService
{
    private readonly ManagementOperationRegistry _registry = ManagementOperationRegistry.BuiltIn;
    public ManagementCustomerIdentityAuthorizationService(IManagementApplicationAccessService access,
    IManagementCustomerIdentityAvailabilityService availability, IServiceOperationExecutor operations, ManagementOperationRegistry registry)
        : this(access, availability, operations) => _registry = registry;

    public Task RequireAsync(Guid applicationId, string operation, CancellationToken ct = default)
        => operations.ExecuteAsync(this, async token =>
        {
            var required = _registry.RequiredFor(operation);
            foreach (var permission in required) await access.RequireAsync(applicationId, permission, token);
            var current = await availability.GetAsync(applicationId, token);
            if (current.Status != ManagementCustomerIdentityStatuses.Available || current.Info is null ||
                required.Any(x => !current.Info.Capabilities.Contains(x, StringComparer.Ordinal))) throw new ManagementAccessDeniedException();
            foreach (var permission in required) await access.RequireAsync(applicationId, permission, token);
        }, new ServiceOperationExecutionOptions { OperationName = "management.portal.customer-identity.authorize",
            AuditAction = "management.portal.customer-identity.authorize", EntityId = applicationId,
            EntityName = "ManagementCustomerIdentityAuthorization", PermissionEnabled = false, AuditEnabled = true }, ct);
}
