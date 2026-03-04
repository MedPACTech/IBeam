namespace IBeam.Identity.Events;

public sealed record AuthUserCreateRequestedEvent : AuthLifecycleEventBase
{
    public const string TypeName = "AuthUserCreateRequestedEvent";
    public AuthUserCreateRequestedEvent() => EventType = TypeName;
    public string? NormalizedEmail { get; init; }
    public string? NormalizedPhone { get; init; }
}

public sealed record TenantCreateRequestedEvent : AuthLifecycleEventBase
{
    public const string TypeName = "TenantCreateRequestedEvent";
    public TenantCreateRequestedEvent() => EventType = TypeName;
    public string AuthUserId { get; init; } = string.Empty;
    public string? SuggestedTenantName { get; init; }
}

public sealed record TenantUserLinkRequestedEvent : AuthLifecycleEventBase
{
    public const string TypeName = "TenantUserLinkRequestedEvent";
    public TenantUserLinkRequestedEvent() => EventType = TypeName;
    public Guid TenantId { get; init; }
    public string AuthUserId { get; init; } = string.Empty;
}

public sealed record LoginAttemptedEvent : AuthLifecycleEventBase
{
    public const string TypeName = "LoginAttemptedEvent";
    public LoginAttemptedEvent() => EventType = TypeName;
    public string Method { get; init; } = string.Empty; // otp|password|oauth
    public string? Identifier { get; init; } // normalized email/phone/provider-sub
}

public sealed record LoginSucceededEvent : AuthLifecycleEventBase
{
    public const string TypeName = "LoginSucceededEvent";
    public LoginSucceededEvent() => EventType = TypeName;
    public string Method { get; init; } = string.Empty;
    public string AuthUserId { get; init; } = string.Empty;
    public Guid? TenantId { get; init; }
    public bool RequiresTenantSelection { get; init; }
}

public sealed record LoginFailedEvent : AuthLifecycleEventBase
{
    public const string TypeName = "LoginFailedEvent";
    public LoginFailedEvent() => EventType = TypeName;
    public string Method { get; init; } = string.Empty;
    public string? Identifier { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed record OtpChallengeCreatedEvent : AuthLifecycleEventBase
{
    public const string TypeName = "OtpChallengeCreatedEvent";
    public OtpChallengeCreatedEvent() => EventType = TypeName;
    public string ChallengeId { get; init; } = string.Empty;
    public string Destination { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
}

public sealed record OtpVerifiedEvent : AuthLifecycleEventBase
{
    public const string TypeName = "OtpVerifiedEvent";
    public OtpVerifiedEvent() => EventType = TypeName;
    public string ChallengeId { get; init; } = string.Empty;
}

public sealed record OtpVerificationFailedEvent : AuthLifecycleEventBase
{
    public const string TypeName = "OtpVerificationFailedEvent";
    public OtpVerificationFailedEvent() => EventType = TypeName;
    public string ChallengeId { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

public sealed record TokenIssuedEvent : AuthLifecycleEventBase
{
    public const string TypeName = "TokenIssuedEvent";
    public TokenIssuedEvent() => EventType = TypeName;
    public string TokenKind { get; init; } = string.Empty; // access|pretenant|refresh-rotated
    public string AuthUserId { get; init; } = string.Empty;
    public Guid? TenantId { get; init; }
    public string? SessionId { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }
}

public sealed record RefreshTokenRotatedEvent : AuthLifecycleEventBase
{
    public const string TypeName = "RefreshTokenRotatedEvent";
    public RefreshTokenRotatedEvent() => EventType = TypeName;
    public string AuthUserId { get; init; } = string.Empty;
    public Guid TenantId { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public DateTimeOffset RefreshTokenExpiresAtUtc { get; init; }
}

public sealed record SessionRevokedEvent : AuthLifecycleEventBase
{
    public const string TypeName = "SessionRevokedEvent";
    public SessionRevokedEvent() => EventType = TypeName;
    public string AuthUserId { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
}

