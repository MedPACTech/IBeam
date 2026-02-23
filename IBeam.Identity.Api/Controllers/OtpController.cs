using System.Security.Claims;
using IBeam.Identity.Services.Auth.Contracts;
using IBeam.Identity.Services.Otp.Contracts;
using IBeam.Identity.Services.Otp.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.Identity.Api.Controllers;

[ApiController]
[Route("api/otp")]
public sealed class OtpController : ControllerBase
{
    private readonly IOtpService _otp;

    public OtpController(IOtpService otp) => _otp = otp;

    /// <summary>
    /// Starts an OTP challenge (send code) for a given purpose/channel.
    /// </summary>
    [HttpPost("challenges")]
    public async Task<IActionResult> CreateChallenge([FromBody] CreateOtpChallengeRequest req, CancellationToken ct)
    {
        var result = await _otp.CreateChallengeAsync(req, ct);
        return Ok(result);
    }

    /// <summary>
    /// Verifies an OTP code against a previously created challenge.
    /// </summary>
    [HttpPost("challenges/{challengeId:guid}/verify")]
    public async Task<IActionResult> Verify([FromRoute] Guid challengeId, [FromBody] VerifyOtpChallengeRequest req, CancellationToken ct)
    {
        // Ensure route is the source of truth
        req = req with { ChallengeId = challengeId };

        var result = await _otp.VerifyChallengeAsync(req, ct);
        return Ok(result);
    }

    /// <summary>
    /// Resends an OTP code for an existing challenge (enforces cooldown/throttle in service).
    /// </summary>
    [HttpPost("challenges/{challengeId:guid}/resend")]
    public async Task<IActionResult> Resend([FromRoute] Guid challengeId, CancellationToken ct)
    {
        var result = await _otp.ResendChallengeAsync(challengeId, ct);
        return Ok(result);
    }
}
