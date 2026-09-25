using System.Security.Claims;
using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface IOAuthDeviceAuthorizationService
{
    Task<OAuthDeviceAuthorizationStartResult> RequestAsync(OAuthDeviceAuthorizationRequest request, CancellationToken ct = default);
    Task<OAuthDeviceApprovalContext> PrepareApprovalAsync(ClaimsPrincipal subject, string userCode, CancellationToken ct = default);
    Task<OAuthDeviceApprovalResult> ApproveAsync(ClaimsPrincipal subject, OAuthDeviceApprovalDecision decision, CancellationToken ct = default);
}
