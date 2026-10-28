using IBeam.Identity.Events;

namespace IBeam.Identity.Interfaces;

/// <summary>One metadata projection for native and Management-origin committed changes. No publication or mutation.</summary>
public interface IIdentityTenantMembershipEventFactory
{
    Task<TenantMembershipChangedEvent?> ProjectAsync(IdentityMembershipCommittedChange committed, CancellationToken ct = default);
}
/// <summary>Optional native persistence port. Protocol 1 promises atomic membership/fence/event intent, an immutable
/// original event per logical commit, monotonic per-tenant sequence and bounded reads. Unsupported adapters must
/// advertise protocol 0 / Durable=false and cannot claim durable event delivery. Reading never repairs or mutates.</summary>
public interface IIdentityTenantMembershipCommittedEventReader
{
    int ProtocolVersion { get; }
    bool Durable { get; }
    Task<TenantMembershipChangedEvent?> FindByOperationAsync(IdentityMembershipEventScope scope, Guid operationId, CancellationToken ct = default);
    Task<IdentityMembershipEventPage> ReadPageAsync(IdentityMembershipEventScope scope, long afterSequence, int maximumEvents, CancellationToken ct = default);
}
