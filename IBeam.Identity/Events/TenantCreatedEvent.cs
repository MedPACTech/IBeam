namespace IBeam.Identity.Events;

public sealed record TenantCreatedEvent : AuthLifecycleEventBase
{
    public const string TypeName = "TenantCreatedEvent";

    public TenantCreatedEvent()
    {
        EventType = TypeName;
    }

    public Guid TenantId { get; init; }
    public string? TenantName { get; init; }
}

