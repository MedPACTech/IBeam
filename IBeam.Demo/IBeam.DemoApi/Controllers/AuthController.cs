using System.Security.Claims;
using IBeam.Identity.Core.Auth.Contracts;
using IBeam.Identity.Core.Auth.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.DemoApi.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth) => _auth = auth;

    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req, CancellationToken ct)
    {
        await _auth.RegisterAsync(req, ct);
        return NoContent();
    }

    [HttpPost("password-login")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> PasswordLogin([FromBody] PasswordLoginRequest req, CancellationToken ct)
    {
        var result = await _auth.PasswordLoginAsync(req, ct);
        return Ok(result);
    }

    [Authorize]
    [HttpPost("select-tenant")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SelectTenant([FromBody] SelectTenantRequest req, CancellationToken ct)
    {
        var userId = GetUserIdOrNull();
        if (userId is null) return Unauthorized();

        var token = await _auth.SelectTenantAsync(userId, req, ct);
        return Ok(token);
    }

    [Authorize]
    [HttpPost("switch-tenant")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SwitchTenant([FromBody] SelectTenantRequest req, CancellationToken ct)
    {
        var userId = GetUserIdOrNull();
        if (userId is null) return Unauthorized();

        if (IsPreTenantToken()) return Forbid();

        var token = await _auth.SwitchTenantAsync(userId, req, ct);
        return Ok(token);
    }

    private string? GetUserIdOrNull()
        => User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

    private bool IsPreTenantToken()
        => string.Equals(User.FindFirstValue("pt"), "1", StringComparison.Ordinal);
}
