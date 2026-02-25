using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using IBeam.Identity.Abstractions.Interfaces;
using IBeam.Identity.Abstractions.Models;

namespace IBeam.Identity.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IIdentityOtpAuthService _otpAuth;

    public AuthController(IIdentityOtpAuthService otpAuth)
    {

        _otpAuth = otpAuth;
    }

   
    [HttpPost("register-otp")]
    public async Task<IActionResult> RegisterWithOtp([FromBody] RegisterUserOtpRequest req, CancellationToken ct)
    {
        // RegisterUserOtpRequest should have a Destination (email or phone)
        var result = await _otpAuth.RegisterUserViaOtpAsync(req.Destination, req.TenantId, ct);
        return Ok(result);
    }

    [HttpGet()]
    public async Task<IActionResult> PingAuth(CancellationToken ct)
    {
        // RegisterUserOtpRequest should have a Destination (email or phone)
        var result = true;
        return Ok(result);
    }
}

        // You may need to define this DTO if not already present:
        public class RegisterUserOtpRequest
        {
            public string Destination { get; set; } = string.Empty;
            public Guid? TenantId { get; set; }
        }
