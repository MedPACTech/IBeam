namespace IBeam.Identity.Events;

public sealed record TenantUserLinkedEvent : AuthLifecycleEventBase
{
    public const string TypeName = "TenantUserLinkedEvent";

    public TenantUserLinkedEvent()
    {
        EventType = TypeName;
    }

    public Guid TenantId { get; init; }
    public string AuthUserId { get; init; } = string.Empty;
    public string? Role { get; init; }
    public string? UserTenantId { get; init; }
}

