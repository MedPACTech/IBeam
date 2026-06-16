namespace IBeam.Identity.Events;

public sealed record TenantUpdatedEvent : AuthLifecycleEventBase
{
    public const string TypeName = "TenantUpdatedEvent";

    public TenantUpdatedEvent()
    {
        EventType = TypeName;
    }

    public Guid TenantId { get; init; }
    public string? TenantName { get; init; }
    public string? PreviousTenantName { get; init; }
    public string Status { get; init; } = string.Empty;
}

public sealed record TenantActivatedEvent : AuthLifecycleEventBase
{
    public const string TypeName = "TenantActivatedEvent";

    public TenantActivatedEvent()
    {
        EventType = TypeName;
    }

    public Guid TenantId { get; init; }
    public string? TenantName { get; init; }
}

public sealed record TenantDeactivatedEvent : AuthLifecycleEventBase
{
    public const string TypeName = "TenantDeactivatedEvent";

    public TenantDeactivatedEvent()
    {
        EventType = TypeName;
    }

    public Guid TenantId { get; init; }
    public string? TenantName { get; init; }
}
