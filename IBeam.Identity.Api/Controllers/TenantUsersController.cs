using System.Security.Claims;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.Identity.Api.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public sealed class TenantUsersController : ControllerBase
{
    private static readonly string[] ManageTenantClaims = ["owner", "administrator", "admin"];

    private readonly ITenantMembershipStore _memberships;
    private readonly ITenantRoleService _roles;

    public TenantUsersController(ITenantMembershipStore memberships, ITenantRoleService roles)
    {
        _memberships = memberships ?? throw new ArgumentNullException(nameof(memberships));
        _roles = roles ?? throw new ArgumentNullException(nameof(roles));
    }

    [HttpGet("users/me/tenants")]
    public async Task<IActionResult> GetCurrentUserTenants(CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized(new { message = "Authenticated user id claim is missing." });

        return Ok(await _memberships.GetTenantsForUserAsync(userId, ct).ConfigureAwait(false));
    }

    [HttpGet("users/{userId:guid}/tenants")]
    public async Task<IActionResult> GetUserTenants(Guid userId, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
            return Unauthorized(new { message = "Authenticated user id claim is missing." });

        if (currentUserId != userId)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Forbidden." });

        return Ok(await _memberships.GetTenantsForUserAsync(userId, ct).ConfigureAwait(false));
    }

    [HttpGet("tenants/{tenantId:guid}/users")]
    public async Task<IActionResult> GetTenantUsers(Guid tenantId, CancellationToken ct)
    {
        if (!TryAuthorizeTenantAdmin(tenantId, out var forbidden))
            return forbidden;

        return Ok(await _memberships.GetUsersForTenantAsync(tenantId, ct).ConfigureAwait(false));
    }

    [HttpGet("tenants/{tenantId:guid}/users/{userId:guid}")]
    public async Task<IActionResult> GetTenantUser(Guid tenantId, Guid userId, CancellationToken ct)
    {
        if (!TryAuthorizeTenantAdmin(tenantId, out var forbidden))
            return forbidden;

        var membership = await _memberships.GetUserForTenantAsync(tenantId, userId, ct).ConfigureAwait(false);
        return membership is null ? NotFound(new { message = "Tenant user not found." }) : Ok(membership);
    }

    [HttpPost("tenants/{tenantId:guid}/users")]
    public async Task<IActionResult> LinkTenantUser(Guid tenantId, [FromBody] LinkTenantUserRequest request, CancellationToken ct)
    {
        if (!TryAuthorizeTenantAdmin(tenantId, out var forbidden))
            return forbidden;

        try
        {
            var result = await _roles.EnsureTenantMembershipAndGrantRolesAsync(
                new TenantMembershipRoleBootstrapRequest(
                    tenantId,
                    request.UserId,
                    request.TenantName,
                    request.RoleIds,
                    request.RoleNames,
                    request.SetAsDefault),
                ct).ConfigureAwait(false);

            return Ok(result);
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [HttpPost("tenants/{tenantId:guid}/users/{userId:guid}/default")]
    public async Task<IActionResult> SetDefaultTenant(Guid tenantId, Guid userId, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
            return Unauthorized(new { message = "Authenticated user id claim is missing." });

        if (currentUserId != userId && !TryAuthorizeTenantAdmin(tenantId, out _))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Forbidden." });

        try
        {
            await _memberships.SetDefaultTenantAsync(userId, tenantId, ct).ConfigureAwait(false);
            return Accepted();
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [HttpPost("tenants/{tenantId:guid}/users/{userId:guid}/disable")]
    public async Task<IActionResult> DisableTenantUser(
        Guid tenantId,
        Guid userId,
        [FromBody] DisableTenantUserRequest? request,
        CancellationToken ct)
    {
        if (!TryAuthorizeTenantAdmin(tenantId, out var forbidden))
            return forbidden;

        try
        {
            await _memberships.DisableTenantMembershipAsync(tenantId, userId, request?.Reason, ct).ConfigureAwait(false);
            return Accepted();
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

public sealed class LinkTenantUserRequest
{
    public Guid UserId { get; set; }
    public string? TenantName { get; set; }
    public List<Guid>? RoleIds { get; set; }
    public List<string>? RoleNames { get; set; }
    public bool SetAsDefault { get; set; }
}

public sealed class DisableTenantUserRequest
{
    public string? Reason { get; set; }
}
