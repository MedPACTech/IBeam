using IBeam.Identity.Api.Authorization;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class TenantInvitesController : ControllerBase
{
    private readonly ITenantInviteService _invites;
    private readonly IOptionsSnapshot<IBeamAccessControlOptions> _accessOptions;

    public TenantInvitesController(
        ITenantInviteService invites,
        IOptionsSnapshot<IBeamAccessControlOptions> accessOptions)
    {
        _invites = invites ?? throw new ArgumentNullException(nameof(invites));
        _accessOptions = accessOptions ?? throw new ArgumentNullException(nameof(accessOptions));
    }

    [Authorize]
    [HttpPost("tenants/{tenantId:guid}/invites")]
    public async Task<IActionResult> CreateInvite(Guid tenantId, [FromBody] TenantInviteCreateRequest request, CancellationToken ct)
    {
        if (!TryAuthorizeTenantAdmin(tenantId, out var forbidden))
            return forbidden;
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized(new { message = "Authenticated user id claim is missing." });

        try
        {
            return Ok(await _invites.CreateInviteAsync(tenantId, request, userId, ct).ConfigureAwait(false));
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
    [HttpGet("tenants/{tenantId:guid}/invites")]
    public async Task<IActionResult> ListInvites(
        Guid tenantId,
        [FromQuery] string? status = null,
        [FromQuery] bool activeOnly = false,
        [FromQuery] bool? includeExpired = null,
        [FromQuery] bool? includeRedeemed = null,
        [FromQuery] bool? includeRevoked = null,
        CancellationToken ct = default)
    {
        if (!TryAuthorizeTenantAdmin(tenantId, out var forbidden))
            return forbidden;

        try
        {
            var request = string.IsNullOrWhiteSpace(status) &&
                          !activeOnly &&
                          !includeExpired.HasValue &&
                          !includeRedeemed.HasValue &&
                          !includeRevoked.HasValue
                ? null
                : new TenantInviteListRequest(status, activeOnly, includeExpired, includeRedeemed, includeRevoked);
            return Ok(await _invites.ListInvitesAsync(tenantId, request, ct).ConfigureAwait(false));
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [Authorize]
    [HttpGet("tenants/{tenantId:guid}/invites/{inviteId:guid}")]
    public async Task<IActionResult> GetInvite(Guid tenantId, Guid inviteId, CancellationToken ct)
    {
        if (!TryAuthorizeTenantAdmin(tenantId, out var forbidden))
            return forbidden;

        var invite = await _invites.GetInviteAsync(tenantId, inviteId, ct).ConfigureAwait(false);
        return invite is null ? NotFound(new { message = "Invite not found." }) : Ok(invite);
    }

    [Authorize]
    [HttpPost("tenants/{tenantId:guid}/invites/{inviteId:guid}/resend")]
    public async Task<IActionResult> ResendInvite(Guid tenantId, Guid inviteId, CancellationToken ct)
    {
        if (!TryAuthorizeTenantAdmin(tenantId, out var forbidden))
            return forbidden;
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized(new { message = "Authenticated user id claim is missing." });

        try
        {
            return Ok(await _invites.ResendInviteAsync(tenantId, inviteId, userId, ct).ConfigureAwait(false));
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [Authorize]
    [HttpPost("tenants/{tenantId:guid}/invites/{inviteId:guid}/revoke")]
    public async Task<IActionResult> RevokeInvite(Guid tenantId, Guid inviteId, [FromBody] RevokeTenantInviteRequest? request, CancellationToken ct)
    {
        if (!TryAuthorizeTenantAdmin(tenantId, out var forbidden))
            return forbidden;
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized(new { message = "Authenticated user id claim is missing." });

        try
        {
            return Ok(await _invites.RevokeInviteAsync(tenantId, inviteId, userId, request?.Reason, ct).ConfigureAwait(false));
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [AllowAnonymous]
    [HttpPost("invites/accept")]
    public async Task<IActionResult> AcceptInvite([FromBody] TenantInviteAcceptRequest request, CancellationToken ct)
    {
        try
        {
            var authenticatedUserId = TryGetCurrentUserId(out var userId) ? userId : (Guid?)null;
            return Ok(await _invites.AcceptInviteAsync(request, authenticatedUserId, ct).ConfigureAwait(false));
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

    [AllowAnonymous]
    [HttpGet("invites/{tokenOrCode}/preview")]
    public async Task<IActionResult> PreviewInvite(string tokenOrCode, CancellationToken ct)
    {
        try
        {
            return Ok(await _invites.PreviewInviteAsync(tokenOrCode, ct).ConfigureAwait(false));
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    private bool TryAuthorizeTenantAdmin(Guid routeTenantId, out ObjectResult forbidden)
    {
        forbidden = StatusCode(StatusCodes.Status403Forbidden, new { message = "Forbidden." });
        return IdentityApiAuthorization.TryAuthorizeTenantOperation(
            User,
            routeTenantId,
            _accessOptions.Value,
            _accessOptions.Value.TenantUserManagementPermissionNames);
    }

    private bool TryGetCurrentUserId(out Guid userId)
        => IdentityApiAuthorization.TryGetCurrentUserId(User, out userId);
}

public sealed class RevokeTenantInviteRequest
{
    public string? Reason { get; set; }
}
