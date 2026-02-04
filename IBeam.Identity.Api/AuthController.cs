using System.Security.Claims;
using IBeam.Identity.Core.Auth.Contracts;
using IBeam.Identity.Core.Auth.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.Identity.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth) => _auth = auth;

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest req)
    {
        await _auth.RegisterAsync(req);
        return Ok();
    }

    [HttpPost("password-login")]
    public async Task<IActionResult> PasswordLogin(PasswordLoginRequest req)
    {
        var result = await _auth.PasswordLoginAsync(req);
        return Ok(result);
    }

    [HttpPost("select-tenant")]
    [Authorize]
    public async Task<IActionResult> SelectTenant(SelectTenantRequest req)
    {
        var userId =
            User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        var token = await _auth.SelectTenantAsync(userId, req);
        return Ok(token);
    }

    [HttpPost("switch-tenant")]
    [Authorize]
    public async Task<IActionResult> SwitchTenant(SelectTenantRequest req, CancellationToken ct)
    {
        var userId =
            User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        // Optional: prevent using a pre-tenant token to switch tenants
        if (User.FindFirstValue("pt") == "1")
            return Forbid();

        var token = await _auth.SwitchTenantAsync(userId, req, ct);
        return Ok(token);
    }


    
}
