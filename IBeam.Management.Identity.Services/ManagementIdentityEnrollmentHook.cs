using IBeam.Identity.Events;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;

namespace IBeam.Management.Identity.Services;

/// <summary>Public enrollment/recovery remains closed. A later validated-invitation workflow can add a narrow grant.</summary>
public sealed class ManagementIdentityEnrollmentHook : IAuthLifecycleHook
{
    public Task OnBeforeAuthUserCreateAsync(AuthUserCreateRequestedEvent evt, CancellationToken ct = default)
        => throw new ManagementAccessDeniedException();

    public Task OnBeforeOtpChallengeCreateAsync(OtpChallengeRequestedEvent evt, CancellationToken ct = default)
    {
        if (evt.Purpose != SenderPurpose.LoginMfa.ToString())
            throw new ManagementAccessDeniedException();
        return Task.CompletedTask;
    }
}
