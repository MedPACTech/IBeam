using System.ComponentModel.DataAnnotations;
using IBeam.Api.Controllers;
using IBeam.Identity.Models;
using IBeam.Management.Api;
using IBeam.Management.Identity.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.Management.Identity.Api;

[Route("api/auth")]
[ServiceFilter(typeof(ManagementIdentityExceptionFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ManagementAuthenticationController(IManagementIdentityAuthenticationService authentication) : ApiControllerBase
{
    [AllowAnonymous, HttpPost("password-login")]
    public async Task<IActionResult> PasswordLoginAsync(ManagementPasswordLoginRequest request, CancellationToken ct)
        => OkResponse(await authentication.PasswordLoginAsync(new PasswordLoginRequest(request.Email, request.Password), ct));

    [AllowAnonymous, HttpPost("2fa/complete-login")]
    public async Task<IActionResult> CompleteMfaAsync(ManagementMfaRequest request, CancellationToken ct)
        => OkResponse(await authentication.CompleteMfaAsync(request.Email, request.ChallengeId, request.Code, ct));

    [AllowAnonymous, HttpPost("refresh")]
    public async Task<IActionResult> RefreshAsync(ManagementRefreshRequest request, CancellationToken ct)
        => OkResponse(await authentication.RefreshAsync(request.RefreshToken, ct));

    [Authorize(Policy = ManagementApiPolicies.Access), HttpPost("logout")]
    public async Task<IActionResult> LogoutAsync(CancellationToken ct)
    {
        await authentication.LogoutAsync(User, ct);
        return NoContentResponse();
    }
}

public sealed class ManagementPasswordLoginRequest
{
    [Required, EmailAddress, StringLength(320)] public string Email { get; set; } = string.Empty;
    [Required, StringLength(4096)] public string Password { get; set; } = string.Empty;
    public override string ToString() => "Management password login (redacted)";
}
public sealed class ManagementMfaRequest
{
    [Required, EmailAddress, StringLength(320)] public string Email { get; set; } = string.Empty;
    [Required, StringLength(128)] public string ChallengeId { get; set; } = string.Empty;
    [Required, StringLength(32)] public string Code { get; set; } = string.Empty;
    public override string ToString() => "Management MFA completion (redacted)";
}
public sealed class ManagementRefreshRequest
{
    [Required, StringLength(4096)] public string RefreshToken { get; set; } = string.Empty;
    public override string ToString() => "Management refresh request (redacted)";
}
