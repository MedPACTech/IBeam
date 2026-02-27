using Microsoft.AspNetCore.Mvc;
using IBeam.Identity.Abstractions.Exceptions;
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
        if (string.IsNullOrWhiteSpace(req.Destination))
            return BadRequest(new { message = "Destination is required." });

        try
        {
            var result = await _otpAuth.RegisterUserViaOtpAsync(req.Destination, req.TenantId, ct);
            return Ok(result);
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [HttpPost("register-otp/complete")]
    public async Task<IActionResult> CompleteRegisterWithOtp([FromBody] CompleteRegisterUserOtpRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ChallengeId))
            return BadRequest(new { message = "ChallengeId is required." });
        if (string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(new { message = "Code is required." });
        if (string.IsNullOrWhiteSpace(req.Destination))
            return BadRequest(new { message = "Destination is required." });

        try
        {
            var result = await _otpAuth.CompleteUserRegistrationViaOtpAsync(
                req.ChallengeId,
                req.Code,
                req.Destination,
                req.DisplayName,
                ct);
            return Ok(result);
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
        catch (IdentityUnauthorizedException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    [HttpGet()]
    public IActionResult PingAuth()
    {
        return Ok(true);
    }
}

public class RegisterUserOtpRequest
{
    public string Destination { get; set; } = string.Empty;
    public Guid? TenantId { get; set; }
}

public class CompleteRegisterUserOtpRequest
{
    public string ChallengeId { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public Guid? TenantId { get; set; }
}
