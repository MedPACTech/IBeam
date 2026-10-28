namespace IBeam.Identity.Events;

/// <summary>Trusted server metadata scope only. Never bind this from a command body. Grants no native permission.</summary>
public static class IdentityMembershipEventActorContext
{
    private static readonly AsyncLocal<IdentityMembershipEventActor?> Value = new();
    public static IdentityMembershipEventActor Current => Value.Value ?? new(IdentityMembershipActorKind.Unknown, null, null, null, null);
    public static IDisposable Push(IdentityMembershipEventActor actor)
    { ArgumentNullException.ThrowIfNull(actor); var prior = Value.Value; Value.Value = actor; return new Restore(prior); }
    private sealed class Restore(IdentityMembershipEventActor? prior) : IDisposable
    { private bool disposed; public void Dispose() { if (!disposed) { Value.Value = prior; disposed = true; } } }
}
