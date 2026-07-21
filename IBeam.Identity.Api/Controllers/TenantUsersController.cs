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
[Authorize]
[Route("api")]
public sealed class TenantUsersController : ControllerBase
{
    private readonly ITenantMembershipStore _memberships;
    private readonly ITenantRoleService _roles;
    private readonly IOptionsSnapshot<IBeamAccessControlOptions> _accessOptions;

    public TenantUsersController(
        ITenantMembershipStore memberships,
        ITenantRoleService roles,
        IOptionsSnapshot<IBeamAccessControlOptions> accessOptions)
    {
        _memberships = memberships ?? throw new ArgumentNullException(nameof(memberships));
        _roles = roles ?? throw new ArgumentNullException(nameof(roles));
        _accessOptions = accessOptions ?? throw new ArgumentNullException(nameof(accessOptions));
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
                    request.SetAsDefault,
                    request.DisplayName ?? request.UserDisplayName,
                    request.Email,
                    request.PhoneNumber),
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
        return IdentityApiAuthorization.TryAuthorizeTenantOperation(
            User,
            routeTenantId,
            _accessOptions.Value,
            _accessOptions.Value.TenantUserManagementPermissionNames);
    }

    private bool TryGetCurrentUserId(out Guid userId)
        => IdentityApiAuthorization.TryGetCurrentUserId(User, out userId);
}

public sealed class LinkTenantUserRequest
{
    public Guid UserId { get; set; }
    public string? TenantName { get; set; }
    public List<Guid>? RoleIds { get; set; }
    public List<string>? RoleNames { get; set; }
    public bool SetAsDefault { get; set; }
    public string? DisplayName { get; set; }
    public string? UserDisplayName { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
}

public sealed class DisableTenantUserRequest
{
    public string? Reason { get; set; }
}
