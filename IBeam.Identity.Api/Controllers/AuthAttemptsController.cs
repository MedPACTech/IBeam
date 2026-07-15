using System.Security.Claims;
using IBeam.Identity.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.Identity.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/auth-attempts")]
public sealed class AuthAttemptsController : ControllerBase
{
    private const string UnlockPermission = "identity:auth-attempts:unlock";

    private static readonly HashSet<string> ManageRoleClaims = new(StringComparer.OrdinalIgnoreCase)
    {
        "owner",
        "admin",
        "administrator",
        "platform-admin",
        "support"
    };

    private readonly IAuthAttemptStore _attempts;
    private readonly IAuthAttemptContextProvider _contextProvider;

    public AuthAttemptsController(
        IAuthAttemptStore attempts,
        IAuthAttemptContextProvider contextProvider)
    {
        _attempts = attempts ?? throw new ArgumentNullException(nameof(attempts));
        _contextProvider = contextProvider ?? throw new ArgumentNullException(nameof(contextProvider));
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

        if (string.Equals(User.FindFirstValue("api_subject_type"), "credential", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(User.FindFirstValue("api_credential_id")))
        {
            return false;
        }

        if (FindClaimValues("permission", "permissions", "scope", "scp")
            .SelectMany(x => ExpandClaimValue(x.Value))
            .Any(x => string.Equals(x, UnlockPermission, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var roleClaims = FindClaimValues("role", "roles", ClaimTypes.Role)
            .SelectMany(x => ExpandClaimValue(x.Value));

        return roleClaims.Any(x => ManageRoleClaims.Contains(x));
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        var raw = User.FindFirstValue("uid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out userId);
    }

    private IEnumerable<Claim> FindClaimValues(params string[] names)
    {
        foreach (var name in names)
        {
            foreach (var claim in User.FindAll(name))
                yield return claim;
        }
    }

    private static IEnumerable<string> ExpandClaimValue(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([',', ' '], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

public sealed class UnlockAuthAttemptRequest
{
    public string Method { get; set; } = "otp";
    public string Identifier { get; set; } = string.Empty;
    public string? Reason { get; set; }
}
