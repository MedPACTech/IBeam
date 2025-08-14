// IBeam.Auditing/AuditEvent.cs
using IBeam.Utilities;
using System;
using System.Collections.Generic;

namespace IBeam.Utilities
{
    public sealed class AuditEvent
    {
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

        // Who did it / context
        public Guid? TenantId { get; init; }
        public Guid? ActorUserId { get; init; }        // user who performed action (optional)
        public string? ActorDisplay { get; init; }     // email/login/name if you have it
        public string? CorrelationId { get; init; }    // trace id / request id

        // What was affected
        public string EntityName { get; init; } = default!; // e.g., "Patient", "Facility", typeof(T).Name
        public Guid EntityId { get; init; }

        // What happened
        public AuditAction Action { get; init; }
        public string? Reason { get; init; }           // optional human-friendly reason

        // Snapshots / extra data (safe to store)
        public object? Data { get; init; }             // serialized later by sink
        public IDictionary<string, object?>? Meta { get; init; } // key/value tags
    }
}
