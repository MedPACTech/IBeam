using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace IBeam.Identity.Api.Controllers;

[ApiController]
public sealed class OAuthDeviceController(IOAuthDeviceAuthorizationService device) : ControllerBase
{
    // RFC 8628 3.1 - the device (CLI) calls this, unauthenticated, to start the flow.
    [AllowAnonymous]
    [HttpPost("/oauth/device_authorization")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Authorize([FromForm] OAuthDeviceAuthorizationHttpRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var result = await device.RequestAsync(new(
                request.ClientId.Trim(),
                SplitScopes(request.Scope),
                request.Resource?.Trim() ?? string.Empty), ct).ConfigureAwait(false);
            return Ok(new
            {
                device_code = result.DeviceCode,
                user_code = result.UserCode,
                verification_uri = result.VerificationUri,
                verification_uri_complete = result.VerificationUriComplete,
                expires_in = result.ExpiresIn,
                interval = result.Interval
            });
        }
        catch (OAuthProtocolException ex)
        {
            return BadRequest(new { error = ex.Error, error_description = ex.Message });
        }
    }

    // The browser SPA's own verification page calls this, authenticated, to show what it's
    // approving before the user commits - mirrors OAuthAuthorizationController.Prepare.
    [Authorize]
    [HttpGet("/oauth/device")]
    public async Task<IActionResult> Prepare([FromQuery(Name = "user_code")] string userCode, CancellationToken ct) =>
        Ok(await device.PrepareApprovalAsync(User, userCode ?? string.Empty, ct).ConfigureAwait(false));

    [Authorize]
    [HttpPost("/oauth/device")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Decide([FromForm] OAuthDeviceApprovalHttpRequest request, CancellationToken ct)
    {
        var result = await device.ApproveAsync(User, new(request.UserCode.Trim(), request.Approved), ct).ConfigureAwait(false);
        return result.Success ? Ok() : BadRequest(new { error = result.Error, error_description = result.ErrorDescription });
    }

    private static IReadOnlyList<string> SplitScopes(string? scope) =>
        (scope ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public sealed class OAuthDeviceAuthorizationHttpRequest
{
    [ModelBinder(Name = "client_id")] public string ClientId { get; set; } = string.Empty;
    [ModelBinder(Name = "scope")] public string? Scope { get; set; }
    [ModelBinder(Name = "resource")] public string? Resource { get; set; }
}

public sealed class OAuthDeviceApprovalHttpRequest
{
    [ModelBinder(Name = "user_code")] public string UserCode { get; set; } = string.Empty;
    [ModelBinder(Name = "approved")] public bool Approved { get; set; }
}
