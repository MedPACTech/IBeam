using IBeam.Identity.Events;

namespace IBeam.Identity.Interfaces;

public interface IAuthLifecycleHook
{
    Task OnBeforeAuthUserCreateAsync(AuthUserCreateRequestedEvent evt, CancellationToken ct = default)
        => Task.CompletedTask;
    Task OnAuthUserCreatedAsync(AuthUserCreatedEvent evt, CancellationToken ct = default)
        => Task.CompletedTask;

    Task OnBeforeTenantCreateAsync(TenantCreateRequestedEvent evt, CancellationToken ct = default)
        => Task.CompletedTask;
    Task OnTenantCreatedAsync(TenantCreatedEvent evt, CancellationToken ct = default)
        => Task.CompletedTask;

    Task OnBeforeTenantUserLinkAsync(TenantUserLinkRequestedEvent evt, CancellationToken ct = default)
        => Task.CompletedTask;
    Task OnTenantUserLinkedAsync(TenantUserLinkedEvent evt, CancellationToken ct = default)
        => Task.CompletedTask;
    Task OnBeforeTenantSelectionAsync(TenantSelectionRequestedEvent evt, CancellationToken ct = default)
        => Task.CompletedTask;
    Task OnTenantSelectedAsync(TenantSelectedEvent evt, CancellationToken ct = default)
        => Task.CompletedTask;

    Task OnBeforeLoginAsync(LoginAttemptedEvent evt, CancellationToken ct = default)
        => Task.CompletedTask;
    Task OnLoginSucceededAsync(LoginSucceededEvent evt, CancellationToken ct = default)
        => Task.CompletedTask;
    Task OnLoginFailedAsync(LoginFailedEvent evt, CancellationToken ct = default)
        => Task.CompletedTask;

    Task OnOtpChallengeCreatedAsync(OtpChallengeCreatedEvent evt, CancellationToken ct = default)
        => Task.CompletedTask;
    Task OnBeforeOtpChallengeCreateAsync(OtpChallengeRequestedEvent evt, CancellationToken ct = default)
        => Task.CompletedTask;
    Task OnBeforeOtpVerifyAsync(OtpVerifyRequestedEvent evt, CancellationToken ct = default)
        => Task.CompletedTask;
    Task OnOtpVerifiedAsync(OtpVerifiedEvent evt, CancellationToken ct = default)
        => Task.CompletedTask;
    Task OnOtpVerificationFailedAsync(OtpVerificationFailedEvent evt, CancellationToken ct = default)
        => Task.CompletedTask;

    Task OnBeforeTokenIssueAsync(TokenIssueRequestedEvent evt, CancellationToken ct = default)
        => Task.CompletedTask;
    Task OnTokenIssuedAsync(TokenIssuedEvent evt, CancellationToken ct = default)
        => Task.CompletedTask;
    Task OnBeforeRefreshTokenRotateAsync(RefreshTokenRotateRequestedEvent evt, CancellationToken ct = default)
        => Task.CompletedTask;
    Task OnRefreshTokenRotatedAsync(RefreshTokenRotatedEvent evt, CancellationToken ct = default)
        => Task.CompletedTask;
    Task OnBeforeSessionRevokeAsync(SessionRevokeRequestedEvent evt, CancellationToken ct = default)
        => Task.CompletedTask;
    Task OnSessionRevokedAsync(SessionRevokedEvent evt, CancellationToken ct = default)
        => Task.CompletedTask;
}
