using System.Security.Claims;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.Identity.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class TenantInvitesController : ControllerBase
{
    private static readonly string[] ManageTenantClaims = ["owner", "administrator", "admin"];

    private readonly ITenantInviteService _invites;

    public TenantInvitesController(ITenantInviteService invites)
    {
        _invites = invites ?? throw new ArgumentNullException(nameof(invites));
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
    public async Task<IActionResult> ListInvites(Guid tenantId, CancellationToken ct)
    {
        if (!TryAuthorizeTenantAdmin(tenantId, out var forbidden))
            return forbidden;

        return Ok(await _invites.ListInvitesAsync(tenantId, ct).ConfigureAwait(false));
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

        var claimTenant = User.FindFirstValue("tid");
        if (!Guid.TryParse(claimTenant, out var tokenTenantId))
            return false;

        if (tokenTenantId != routeTenantId)
            return false;

        var roleClaims = User.FindAll("role").Select(x => x.Value).ToList();
        return roleClaims.Any(x => ManageTenantClaims.Contains(x, StringComparer.OrdinalIgnoreCase));
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        userId = Guid.Empty;
        var raw = User.FindFirstValue("uid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out userId);
    }
}

public sealed class RevokeTenantInviteRequest
{
    public string? Reason { get; set; }
}
