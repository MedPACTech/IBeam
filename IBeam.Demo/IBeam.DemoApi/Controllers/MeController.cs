using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.DemoApi.Controllers;

[ApiController]
[Route("api/me")]
public sealed class MeController : ControllerBase
{
  [Authorize]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status401Unauthorized)]

    [HttpGet("whoami")]
    public IActionResult WhoAmI()
    {
        var userId =
            User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        var email =
            User.FindFirstValue(ClaimTypes.Email) ??
            User.FindFirstValue("email");

        var tenantId =
            User.FindFirstValue("tenant_id") ??
            User.FindFirstValue("tid");

        var isPreTenant =
            string.Equals(User.FindFirstValue("pt"), "1", StringComparison.Ordinal);

        var roles = User.FindAll(ClaimTypes.Role)
            .Select(r => r.Value)
            .ToArray();

        var claims = User.Claims
            .Select(c => new { c.Type, c.Value })
            .ToArray();

        return Ok(new
        {
            userId,
            email,
            tenantId,
            isPreTenant,
            roles,
            claims
        });
    }
}
