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
    private readonly IIdentityAuthService _passwordAuth;

    public AuthController(IIdentityOtpAuthService otpAuth, IIdentityAuthService passwordAuth)
    {

        _otpAuth = otpAuth;
        _passwordAuth = passwordAuth;
    }

    [HttpPost("startotp")]
    public async Task<IActionResult> StartOtp([FromBody] StartOtpRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Destination))
            return BadRequest(new { message = "Destination is required." });

        try
        {
            var result = await _otpAuth.StartOtpAsync(req.Destination, req.TenantId, ct);
            return Ok(result);
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [HttpPost("completeotp")]
    public async Task<IActionResult> CompleteOtp([FromBody] CompleteOtpRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ChallengeId))
            return BadRequest(new { message = "ChallengeId is required." });
        if (string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(new { message = "Code is required." });
        if (string.IsNullOrWhiteSpace(req.Destination))
            return BadRequest(new { message = "Destination is required." });

        try
        {
            var result = await _otpAuth.CompleteOtpAsync(
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

    [HttpPost("start-email-password-registration")]
    public async Task<IActionResult> StartEmailPasswordRegistration([FromBody] StartEmailPasswordRegistrationRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Email))
            return BadRequest(new { message = "Email is required." });

        try
        {
            var result = await _passwordAuth.StartEmailPasswordRegistrationAsync(
                req.Email,
                req.DisplayName,
                req.ResetUrlBase,
                ct);

            return Ok(result);
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [HttpPost("complete-email-password-registration")]
    public async Task<IActionResult> CompleteEmailPasswordRegistration([FromBody] CompleteEmailPasswordRegistrationRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Email))
            return BadRequest(new { message = "Email is required." });
        if (string.IsNullOrWhiteSpace(req.ChallengeId))
            return BadRequest(new { message = "ChallengeId is required." });
        if (string.IsNullOrWhiteSpace(req.VerificationToken))
            return BadRequest(new { message = "VerificationToken is required." });
        if (string.IsNullOrWhiteSpace(req.NewPassword))
            return BadRequest(new { message = "NewPassword is required." });

        try
        {
            var result = await _passwordAuth.CompleteEmailPasswordRegistrationAsync(
                req.Email,
                req.ChallengeId,
                req.VerificationToken,
                req.NewPassword,
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

    [HttpPost("password-login")]
    public async Task<IActionResult> PasswordLogin([FromBody] PasswordLoginApiRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Email))
            return BadRequest(new { message = "Email is required." });
        if (string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { message = "Password is required." });

        try
        {
            var result = await _passwordAuth.PasswordLoginAsync(
                new PasswordLoginRequest(req.Email, req.Password),
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

public class StartOtpRequest
{
    public string Destination { get; set; } = string.Empty;
    public Guid? TenantId { get; set; }
}

public class CompleteOtpRequest
{
    public string ChallengeId { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
}

public class StartEmailPasswordRegistrationRequest
{
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? ResetUrlBase { get; set; }
}

public class CompleteEmailPasswordRegistrationRequest
{
    public string Email { get; set; } = string.Empty;
    public string ChallengeId { get; set; } = string.Empty;
    public string VerificationToken { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
}

public class PasswordLoginApiRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
