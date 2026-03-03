using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace IBeam.Identity.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IIdentityOtpAuthService _otpAuth;
    private readonly IIdentityAuthService _passwordAuth;
    private readonly IIdentityOAuthAuthService _oauthAuth;
    private readonly ITokenService _tokens;
    private readonly FeatureOptions _features;

    public AuthController(
        IIdentityOtpAuthService otpAuth,
        IIdentityAuthService passwordAuth,
        IIdentityOAuthAuthService oauthAuth,
        ITokenService tokens,
        IOptionsSnapshot<FeatureOptions> features)
    {

        _otpAuth = otpAuth;
        _passwordAuth = passwordAuth;
        _oauthAuth = oauthAuth;
        _tokens = tokens;
        _features = features.Value;
    }

    [HttpPost("startotp")]
    public async Task<IActionResult> StartOtp([FromBody] StartOtpRequest req, CancellationToken ct)
    {
        if (!_features.Otp) return NotFound(new { message = "OTP authentication is disabled." });

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
        if (!_features.Otp) return NotFound(new { message = "OTP authentication is disabled." });

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
        if (!_features.PasswordAuth) return NotFound(new { message = "Password authentication is disabled." });

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
        if (!_features.PasswordAuth) return NotFound(new { message = "Password authentication is disabled." });

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
        if (!_features.PasswordAuth) return NotFound(new { message = "Password authentication is disabled." });

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

    [Authorize]
    [HttpPost("2fa/setup/start")]
    public async Task<IActionResult> StartTwoFactorSetup([FromBody] StartTwoFactorSetupRequest req, CancellationToken ct)
    {
        if (!_features.PasswordAuth || !_features.TwoFactor)
            return NotFound(new { message = "Two-factor authentication is disabled." });

        if (string.IsNullOrWhiteSpace(req.Method))
            return BadRequest(new { message = "Method is required (email or sms)." });

        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized(new { message = "Authenticated user id claim is missing." });

        try
        {
            var result = await _passwordAuth.StartTwoFactorSetupAsync(userId, req.Method, ct);
            return Ok(result);
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [Authorize]
    [HttpPost("2fa/setup/complete")]
    public async Task<IActionResult> CompleteTwoFactorSetup([FromBody] CompleteTwoFactorSetupRequest req, CancellationToken ct)
    {
        if (!_features.PasswordAuth || !_features.TwoFactor)
            return NotFound(new { message = "Two-factor authentication is disabled." });

        if (string.IsNullOrWhiteSpace(req.Method))
            return BadRequest(new { message = "Method is required (email or sms)." });
        if (string.IsNullOrWhiteSpace(req.ChallengeId))
            return BadRequest(new { message = "ChallengeId is required." });
        if (string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(new { message = "Code is required." });

        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized(new { message = "Authenticated user id claim is missing." });

        try
        {
            await _passwordAuth.CompleteTwoFactorSetupAsync(userId, req.Method, req.ChallengeId, req.Code, ct);
            return Ok(new { enabled = true, method = req.Method.Trim().ToLowerInvariant() });
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [HttpPost("2fa/complete-login")]
    public async Task<IActionResult> CompleteTwoFactorLogin([FromBody] CompleteTwoFactorLoginRequest req, CancellationToken ct)
    {
        if (!_features.PasswordAuth || !_features.TwoFactor)
            return NotFound(new { message = "Two-factor authentication is disabled." });

        if (string.IsNullOrWhiteSpace(req.Email))
            return BadRequest(new { message = "Email is required." });
        if (string.IsNullOrWhiteSpace(req.ChallengeId))
            return BadRequest(new { message = "ChallengeId is required." });
        if (string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(new { message = "Code is required." });

        try
        {
            var result = await _passwordAuth.CompleteTwoFactorLoginAsync(req.Email, req.ChallengeId, req.Code, ct);
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

    [Authorize]
    [HttpPost("2fa/disable")]
    public async Task<IActionResult> DisableTwoFactor(CancellationToken ct)
    {
        if (!_features.PasswordAuth || !_features.TwoFactor)
            return NotFound(new { message = "Two-factor authentication is disabled." });

        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized(new { message = "Authenticated user id claim is missing." });

        try
        {
            await _passwordAuth.DisableTwoFactorAsync(userId, ct);
            return Ok(new { enabled = false });
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [Authorize]
    [HttpPost("2fa/method")]
    public async Task<IActionResult> SetPreferredTwoFactorMethod([FromBody] SetPreferredTwoFactorMethodRequest req, CancellationToken ct)
    {
        if (!_features.PasswordAuth || !_features.TwoFactor)
            return NotFound(new { message = "Two-factor authentication is disabled." });

        if (string.IsNullOrWhiteSpace(req.Method))
            return BadRequest(new { message = "Method is required (email or sms)." });

        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized(new { message = "Authenticated user id claim is missing." });

        try
        {
            await _passwordAuth.SetPreferredTwoFactorMethodAsync(userId, req.Method, ct);
            return Ok(new { enabled = true, method = req.Method.Trim().ToLowerInvariant() });
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [HttpGet("oauth/start")]
    public async Task<IActionResult> StartOAuth([FromQuery] string provider, [FromQuery] string redirectUri, CancellationToken ct)
    {
        if (!_features.OAuth) return NotFound(new { message = "OAuth authentication is disabled." });

        try
        {
            var result = await _oauthAuth.StartOAuthAsync(provider, redirectUri, ct);
            return Ok(result);
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [HttpPost("oauth/complete")]
    public async Task<IActionResult> CompleteOAuth([FromBody] OAuthCallbackApiRequest req, CancellationToken ct)
    {
        if (!_features.OAuth) return NotFound(new { message = "OAuth authentication is disabled." });

        try
        {
            var result = await _oauthAuth.CompleteOAuthAsync(
                new OAuthCallbackRequest(req.Provider, req.State, req.Code, req.RedirectUri),
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

    [Authorize]
    [HttpGet("oauth/link/start")]
    public async Task<IActionResult> StartOAuthLink([FromQuery] string provider, [FromQuery] string redirectUri, CancellationToken ct)
    {
        if (!_features.OAuth) return NotFound(new { message = "OAuth authentication is disabled." });
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized(new { message = "Authenticated user id claim is missing." });

        try
        {
            var result = await _oauthAuth.StartOAuthLinkAsync(userId, provider, redirectUri, ct);
            return Ok(result);
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [Authorize]
    [HttpPost("oauth/link/complete")]
    public async Task<IActionResult> CompleteOAuthLink([FromBody] OAuthCallbackApiRequest req, CancellationToken ct)
    {
        if (!_features.OAuth) return NotFound(new { message = "OAuth authentication is disabled." });
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized(new { message = "Authenticated user id claim is missing." });

        try
        {
            await _oauthAuth.LinkOAuthAsync(userId, new OAuthCallbackRequest(req.Provider, req.State, req.Code, req.RedirectUri), ct);
            return Ok(new { linked = true, provider = req.Provider.Trim().ToLowerInvariant() });
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [Authorize]
    [HttpPost("oauth/unlink")]
    public async Task<IActionResult> UnlinkOAuth([FromBody] OAuthUnlinkRequest req, CancellationToken ct)
    {
        if (!_features.OAuth) return NotFound(new { message = "OAuth authentication is disabled." });
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized(new { message = "Authenticated user id claim is missing." });
        if (string.IsNullOrWhiteSpace(req.Provider))
            return BadRequest(new { message = "Provider is required." });

        try
        {
            await _oauthAuth.UnlinkOAuthAsync(userId, req.Provider, ct);
            return Ok(new { linked = false, provider = req.Provider.Trim().ToLowerInvariant() });
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [Authorize]
    [HttpGet("oauth/linked")]
    public async Task<IActionResult> GetLinkedOAuthProviders(CancellationToken ct)
    {
        if (!_features.OAuth) return NotFound(new { message = "OAuth authentication is disabled." });
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized(new { message = "Authenticated user id claim is missing." });

        try
        {
            var result = await _oauthAuth.GetLinkedProvidersAsync(userId, ct);
            return Ok(result);
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.RefreshToken))
            return BadRequest(new { message = "RefreshToken is required." });

        try
        {
            var token = await _tokens.RefreshAccessTokenAsync(req.RefreshToken, ct);
            return Ok(token);
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

    [Authorize]
    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions(CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized(new { message = "Authenticated user id claim is missing." });

        try
        {
            var sessions = await _tokens.GetUserSessionsAsync(userId, ct);
            return Ok(sessions);
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [Authorize]
    [HttpPost("sessions/revoke")]
    public async Task<IActionResult> RevokeSession([FromBody] RevokeSessionRequest req, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized(new { message = "Authenticated user id claim is missing." });
        if (string.IsNullOrWhiteSpace(req.SessionId))
            return BadRequest(new { message = "SessionId is required." });

        try
        {
            var revoked = await _tokens.RevokeSessionAsync(userId, req.SessionId, ct);
            return Ok(new { revoked, sessionId = req.SessionId });
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [HttpGet()]
    public IActionResult PingAuth()
    {
        return Ok(true);
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        userId = Guid.Empty;
        var raw = User.FindFirstValue("uid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out userId);
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

public class StartTwoFactorSetupRequest
{
    public string Method { get; set; } = string.Empty;
}

public class CompleteTwoFactorSetupRequest
{
    public string Method { get; set; } = string.Empty;
    public string ChallengeId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

public class CompleteTwoFactorLoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string ChallengeId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

public class SetPreferredTwoFactorMethodRequest
{
    public string Method { get; set; } = string.Empty;
}

public class OAuthCallbackApiRequest
{
    public string Provider { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
}

public class OAuthUnlinkRequest
{
    public string Provider { get; set; } = string.Empty;
}
