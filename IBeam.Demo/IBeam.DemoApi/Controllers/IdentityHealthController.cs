using IBeam.Identity.Services.Auth.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.DemoApi.Controllers;

[ApiController]
[Route("api/identity")]
public sealed class IdentityHealthController : ControllerBase
{
    [HttpGet("ping")]
    public IActionResult Ping([FromServices] IAuthService authService)
        => Ok(new { ok = true, authService = authService.GetType().FullName });
}
