using IBeam.Identity.Api.Authorization;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/auth-attempts")]
public sealed class AuthAttemptsController : ControllerBase
{
    private readonly IAuthAttemptStore _attempts;
    private readonly IAuthAttemptContextProvider _contextProvider;
    private readonly IOptionsSnapshot<IBeamAccessControlOptions> _accessOptions;

    public AuthAttemptsController(
        IAuthAttemptStore attempts,
        IAuthAttemptContextProvider contextProvider,
        IOptionsSnapshot<IBeamAccessControlOptions> accessOptions)
    {
        _attempts = attempts ?? throw new ArgumentNullException(nameof(attempts));
        _contextProvider = contextProvider ?? throw new ArgumentNullException(nameof(contextProvider));
        _accessOptions = accessOptions ?? throw new ArgumentNullException(nameof(accessOptions));
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string method,
        [FromQuery] string identifier,
        CancellationToken ct)
    {
        if (!TryAuthorizeAuthAttemptAdmin(out var forbidden))
            return forbidden;

        if (string.IsNullOrWhiteSpace(method))
            return BadRequest(new { message = "Method is required." });
        if (string.IsNullOrWhiteSpace(identifier))
            return BadRequest(new { message = "Identifier is required." });

        var state = await _attempts.GetStateAsync(method, identifier, ct).ConfigureAwait(false);
        return Ok(state);
    }

    [HttpPost("unlock")]
    public async Task<IActionResult> Unlock([FromBody] UnlockAuthAttemptRequest request, CancellationToken ct)
    {
        if (!TryAuthorizeAuthAttemptAdmin(out var forbidden))
            return forbidden;

        if (string.IsNullOrWhiteSpace(request.Method))
            return BadRequest(new { message = "Method is required." });
        if (string.IsNullOrWhiteSpace(request.Identifier))
            return BadRequest(new { message = "Identifier is required." });

        var unlockedBy = TryGetCurrentUserId(out var userId) ? userId : (Guid?)null;
        var state = await _attempts.UnlockAsync(
                request.Method,
                request.Identifier,
                unlockedBy,
                request.Reason,
                ct,
                _contextProvider.GetCurrent())
            .ConfigureAwait(false);

        return Ok(state);
    }

    [HttpDelete]
    public async Task<IActionResult> Clear(
        [FromQuery] string method,
        [FromQuery] string identifier,
        CancellationToken ct)
    {
        if (!TryAuthorizeAuthAttemptAdmin(out var forbidden))
            return forbidden;

        if (string.IsNullOrWhiteSpace(method))
            return BadRequest(new { message = "Method is required." });
        if (string.IsNullOrWhiteSpace(identifier))
            return BadRequest(new { message = "Identifier is required." });

        await _attempts.ClearAsync(method, identifier, ct).ConfigureAwait(false);
        return NoContent();
    }

    private bool TryAuthorizeAuthAttemptAdmin(out ObjectResult forbidden)
    {
        forbidden = StatusCode(StatusCodes.Status403Forbidden, new { message = "Forbidden." });
        return IdentityApiAuthorization.TryAuthorizeAuthAttemptAdmin(User, _accessOptions.Value);
    }

    private bool TryGetCurrentUserId(out Guid userId)
        => IdentityApiAuthorization.TryGetCurrentUserId(User, out userId);
}

public sealed class UnlockAuthAttemptRequest
{
    public string Method { get; set; } = "otp";
    public string Identifier { get; set; } = string.Empty;
    public string? Reason { get; set; }
}
