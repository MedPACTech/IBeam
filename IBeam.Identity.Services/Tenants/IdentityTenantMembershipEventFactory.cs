using IBeam.Identity.Events;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Identity.Services.Tenants;

/// <summary>Strict reconstruction of native committed metadata; never publishes an attempted mutation.</summary>
public sealed class IdentityTenantMembershipEventFactory(IServiceOperationExecutor operations) : IIdentityTenantMembershipEventFactory
{
    public Task<TenantMembershipChangedEvent?> ProjectAsync(IdentityMembershipCommittedChange committed, CancellationToken ct = default)
        => operations.ExecuteAsync(this, _ => Task.FromResult(IdentityTenantMembershipEventProtocol.ProjectCommitted(committed)), new ServiceOperationExecutionOptions {
            OperationName = "identity.tenantmemberships.events.project", AuditEnabled = false, PermissionEnabled = false }, ct);

}
public static class IdentityTenantMembershipEventContractExtensions
{
    /// <summary>Registers the shared metadata projector only. Installs no emitter, outbox, dispatcher or grant.</summary>
    public static IServiceCollection AddIBeamIdentityMembershipEventContracts(this IServiceCollection services)
    { services.TryAddScoped<IIdentityTenantMembershipEventFactory, IdentityTenantMembershipEventFactory>(); return services; }
}
