using IBeam.Identity.Services.PasswordReset.Contracts;
using IBeam.Identity.Services.PasswordReset.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.Identity.Api.Controllers;

[ApiController]
[Route("api/password")]
public sealed class PasswordController : ControllerBase
{
    private readonly IPasswordResetService _reset;

    public PasswordController(IPasswordResetService reset) => _reset = reset;

    [HttpPost("reset/requests")]
    public async Task<IActionResult> RequestReset([FromBody] RequestPasswordResetRequest req, CancellationToken ct)
    {
        var result = await _reset.RequestAsync(req, ct);
        // Enumeration-safe: still return OK/Accepted either way
        return Ok(result);
    }

    [HttpPost("reset/confirm")]
    public async Task<IActionResult> Confirm([FromBody] ConfirmPasswordResetRequest req, CancellationToken ct)
    {
        await _reset.ConfirmAsync(req, ct);
        return Ok();
    }

    [HttpPost("reset/validate")]
    public async Task<IActionResult> Validate([FromBody] ValidatePasswordResetTokenRequest req, CancellationToken ct)
    {
        var result = await _reset.ValidateTokenAsync(req, ct);
        return Ok(result);
    }
}
