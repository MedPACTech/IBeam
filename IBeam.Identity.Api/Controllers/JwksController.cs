using IBeam.Identity.Services.Tokens;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.Identity.Api.Controllers;

[ApiController]
public sealed class JwksController(IJwtSigningKeyProvider signingKeys) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("/.well-known/jwks.json")]
    public IActionResult Get() => Ok(signingKeys.GetPublicJwks());
}
