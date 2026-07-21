namespace IBeam.Identity.Events;

public abstract record TenantInviteLifecycleEventBase : AuthLifecycleEventBase
{
    public Guid InviteId { get; init; }
    public Guid TenantId { get; init; }
    public string DestinationType { get; init; } = string.Empty;
    public string NormalizedDestination { get; init; } = string.Empty;
    public override string ToString() => EventType;
}

public sealed record TenantInviteCreateRequestedEvent : TenantInviteLifecycleEventBase
{
    public const string TypeName = "identity.tenantinvite.create.requested";
    public TenantInviteCreateRequestedEvent() => EventType = TypeName;
    public Guid InvitedByUserId { get; init; }
}

public sealed record TenantInviteCreatedEvent : TenantInviteLifecycleEventBase
{
    public const string TypeName = "identity.tenantinvite.created";
    public TenantInviteCreatedEvent() => EventType = TypeName;
    public Guid InvitedByUserId { get; init; }
    public DateTimeOffset ExpiresUtc { get; init; }
}

public sealed record TenantInviteSendRequestedEvent : TenantInviteLifecycleEventBase
{
    public const string TypeName = "identity.tenantinvite.send.requested";
    public TenantInviteSendRequestedEvent() => EventType = TypeName;
}

public sealed record TenantInviteSentEvent : TenantInviteLifecycleEventBase
{
    public const string TypeName = "identity.tenantinvite.sent";
    public TenantInviteSentEvent() => EventType = TypeName;
}

public sealed record TenantInviteAcceptRequestedEvent : TenantInviteLifecycleEventBase
{
    public const string TypeName = "identity.tenantinvite.accept.requested";
    public TenantInviteAcceptRequestedEvent() => EventType = TypeName;
    public string Mode { get; init; } = string.Empty;
}

public sealed record TenantInviteAcceptedEvent : TenantInviteLifecycleEventBase
{
    public const string TypeName = "identity.tenantinvite.accepted";
    public TenantInviteAcceptedEvent() => EventType = TypeName;
    public Guid UserId { get; init; }
    public bool CreatedNewUser { get; init; }
}

public sealed record TenantInviteTenantUserLinkRequestedEvent : TenantInviteLifecycleEventBase
{
    public const string TypeName = "identity.tenantinvite.tenantuserlink.requested";
    public TenantInviteTenantUserLinkRequestedEvent() => EventType = TypeName;
    public Guid UserId { get; init; }
}

public sealed record TenantInviteTenantUserLinkedEvent : TenantInviteLifecycleEventBase
{
    public const string TypeName = "identity.tenantinvite.tenantuserlink.linked";
    public TenantInviteTenantUserLinkedEvent() => EventType = TypeName;
    public Guid UserId { get; init; }
}

public sealed record TenantInviteRolesAssignedEvent : TenantInviteLifecycleEventBase
{
    public const string TypeName = "identity.tenantinvite.roles.assigned";
    public TenantInviteRolesAssignedEvent() => EventType = TypeName;
    public Guid UserId { get; init; }
}

public sealed record TenantInviteRoleAssignmentRequestedEvent : TenantInviteLifecycleEventBase
{
    public const string TypeName = "identity.tenantinvite.roles.assign.requested";
    public TenantInviteRoleAssignmentRequestedEvent() => EventType = TypeName;
    public Guid UserId { get; init; }
}

public sealed record TenantInviteAccessGrantsAssignedEvent : TenantInviteLifecycleEventBase
{
    public const string TypeName = "identity.tenantinvite.accessgrants.assigned";
    public TenantInviteAccessGrantsAssignedEvent() => EventType = TypeName;
    public Guid UserId { get; init; }
    public int GrantCount { get; init; }
}

public sealed record TenantInviteAccessGrantAssignmentRequestedEvent : TenantInviteLifecycleEventBase
{
    public const string TypeName = "identity.tenantinvite.accessgrants.assign.requested";
    public TenantInviteAccessGrantAssignmentRequestedEvent() => EventType = TypeName;
    public Guid UserId { get; init; }
    public int GrantCount { get; init; }
}

public sealed record TenantInviteResendRequestedEvent : TenantInviteLifecycleEventBase
{
    public const string TypeName = "identity.tenantinvite.resend.requested";
    public TenantInviteResendRequestedEvent() => EventType = TypeName;
    public Guid ResentByUserId { get; init; }
}

public sealed record TenantInviteResentEvent : TenantInviteLifecycleEventBase
{
    public const string TypeName = "identity.tenantinvite.resent";
    public TenantInviteResentEvent() => EventType = TypeName;
    public Guid ResentByUserId { get; init; }
}

public sealed record TenantInviteRevokeRequestedEvent : TenantInviteLifecycleEventBase
{
    public const string TypeName = "identity.tenantinvite.revoke.requested";
    public TenantInviteRevokeRequestedEvent() => EventType = TypeName;
    public Guid RevokedByUserId { get; init; }
    public string? Reason { get; init; }
}

public sealed record TenantInviteRevokedEvent : TenantInviteLifecycleEventBase
{
    public const string TypeName = "identity.tenantinvite.revoked";
    public TenantInviteRevokedEvent() => EventType = TypeName;
    public Guid RevokedByUserId { get; init; }
    public string? Reason { get; init; }
}

public sealed record TenantInviteExpirationRequestedEvent : TenantInviteLifecycleEventBase
{
    public const string TypeName = "identity.tenantinvite.expiration.requested";
    public TenantInviteExpirationRequestedEvent() => EventType = TypeName;
}

public sealed record TenantInviteExpiredEvent : TenantInviteLifecycleEventBase
{
    public const string TypeName = "identity.tenantinvite.expired";
    public TenantInviteExpiredEvent() => EventType = TypeName;
}
