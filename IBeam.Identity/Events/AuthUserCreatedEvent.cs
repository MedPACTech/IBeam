namespace IBeam.Identity.Events;

public sealed record AuthUserCreatedEvent : AuthLifecycleEventBase
{
    public const string TypeName = "AuthUserCreatedEvent";

    public AuthUserCreatedEvent()
    {
        EventType = TypeName;
    }

    public required string AuthUserId { get; init; }
    public string? NormalizedEmail { get; init; }
    public string? NormalizedPhone { get; init; }
}

