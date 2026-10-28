using System.Text.Json.Serialization;
using IBeam.Identity.Events;

namespace IBeam.Identity.Interfaces;

/// <summary>One immutable committed event plus mutable dispatch metadata. No membership/business entity snapshots.</summary>
public sealed record IdentityMembershipOutboxEntry(TenantMembershipChangedEvent Event, string State, int Attempts,
    DateTimeOffset NextAttemptUtc, Guid? LeaseId, DateTimeOffset? LeaseExpiresUtc, string? FailureCode)
{
    [JsonIgnore] public string Version { get; init; } = "";
}
public sealed record IdentityMembershipPendingPage(IReadOnlyList<IdentityMembershipEventScope> Scopes, string? Continuation);
public interface IIdentityMembershipEventOutboxStore : IIdentityTenantMembershipCommittedEventReader
{
    Task<IdentityMembershipOutboxEntry?> ReadFirstUndeliveredAsync(IdentityMembershipEventScope scope, CancellationToken ct = default);
    Task<bool> TryReplaceAsync(IdentityMembershipOutboxEntry entry, string expectedVersion, CancellationToken ct = default);
    Task<IdentityMembershipPendingPage> ListPendingScopesAsync(int maximumScopes, string? continuation, CancellationToken ct = default);
    Task RequireMutableTenantAsync(IdentityMembershipEventScope scope, CancellationToken ct = default);
}
/// <summary>Explicit durable receiver/bus acknowledgement; never inferred from a default no-op auth publisher.</summary>
public interface IIdentityMembershipEventDelivery
{
    int ProtocolVersion { get; }
    Task DeliverAsync(TenantMembershipChangedEvent committed, CancellationToken ct = default);
}
public interface IIdentityMembershipEventDispatcher
{
    Task RunAsync(IdentityMembershipEventScope scope, int maximumEvents = 10, CancellationToken ct = default);
    /// <summary>Trusted native repair only; keeps original payload/ID and requires the exact dead-letter version.</summary>
    Task RetryDeadLetterAsync(IdentityMembershipEventScope scope, Guid eventId, string expectedVersion, CancellationToken ct = default);
}
