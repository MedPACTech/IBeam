using IBeam.Identity.Api.Authorization;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/tenants/{tenantId:guid}/roles")]
public sealed class RolesController : ControllerBase
{
    private readonly ITenantRoleService _roles;
    private readonly IOptionsSnapshot<RoleManagementOptions> _roleManagementOptions;
    private readonly IOptionsSnapshot<IBeamAccessControlOptions> _accessOptions;

    public RolesController(
        ITenantRoleService roles,
        IOptionsSnapshot<RoleManagementOptions> roleManagementOptions,
        IOptionsSnapshot<IBeamAccessControlOptions> accessOptions)
    {
        _roles = roles;
        _roleManagementOptions = roleManagementOptions;
        _accessOptions = accessOptions;
    }

    [HttpGet]
    public async Task<IActionResult> GetRoles(Guid tenantId, CancellationToken ct)
    {
        if (!TryAuthorizeTenantRoleAdmin(tenantId, out var forbidden))
            return forbidden;

        var roles = await _roles.GetRolesAsync(tenantId, ct);
        return Ok(roles);
    }

    [HttpGet("{roleId:guid}")]
    public async Task<IActionResult> GetRole(Guid tenantId, Guid roleId, CancellationToken ct)
    {
        if (!TryAuthorizeTenantRoleAdmin(tenantId, out var forbidden))
            return forbidden;

        var role = await _roles.GetRoleAsync(tenantId, roleId, ct);
        if (role is null)
            return NotFound(new { message = "Role not found." });
        return Ok(role);
    }

    [HttpPost]
    public async Task<IActionResult> CreateRole(Guid tenantId, [FromBody] UpsertRoleRequest req, CancellationToken ct)
    {
        if (!TryAuthorizeTenantRoleAdmin(tenantId, out var forbidden))
            return forbidden;
        if (!_roleManagementOptions.Value.AllowTenantRoleCreation)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Tenant role creation is disabled." });

        try
        {
            var role = await _roles.CreateRoleAsync(tenantId, req.Name, ct);
            return Ok(role);
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [HttpPut("{roleId:guid}")]
    public async Task<IActionResult> UpdateRole(Guid tenantId, Guid roleId, [FromBody] UpsertRoleRequest req, CancellationToken ct)
    {
        if (!TryAuthorizeTenantRoleAdmin(tenantId, out var forbidden))
            return forbidden;
        if (!_roleManagementOptions.Value.AllowTenantRoleMutation)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Tenant role mutation is disabled." });

        try
        {
            var role = await _roles.UpdateRoleAsync(tenantId, roleId, req.Name, ct);
            return Ok(role);
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
        catch (IdentityNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpDelete("{roleId:guid}")]
    public async Task<IActionResult> DeleteRole(Guid tenantId, Guid roleId, CancellationToken ct)
    {
        if (!TryAuthorizeTenantRoleAdmin(tenantId, out var forbidden))
            return forbidden;
        if (!_roleManagementOptions.Value.AllowTenantRoleMutation)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Tenant role mutation is disabled." });

        try
        {
            await _roles.DeleteRoleAsync(tenantId, roleId, ct);
            return Accepted();
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
        catch (IdentityNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("/api/tenants/{tenantId:guid}/users/{userId:guid}/roles")]
    public async Task<IActionResult> GetUserRoles(Guid tenantId, Guid userId, CancellationToken ct)
    {
        if (!TryAuthorizeTenantRoleAdmin(tenantId, out var forbidden))
            return forbidden;

        var roles = await _roles.GetRolesForUserAsync(tenantId, userId, ct);
        return Ok(roles);
    }

    [HttpPost("grant")]
    public async Task<IActionResult> GrantRoles(Guid tenantId, [FromBody] UserRoleMutationRequest req, CancellationToken ct)
    {
        if (!TryAuthorizeTenantRoleAdmin(tenantId, out var forbidden))
            return forbidden;

        try
        {
            var result = await _roles.GrantRolesAsync(tenantId, req.UserId, req.RoleIds, ct);
            return Ok(result);
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [HttpPost("revoke")]
    public async Task<IActionResult> RevokeRoles(Guid tenantId, [FromBody] UserRoleMutationRequest req, CancellationToken ct)
    {
        if (!TryAuthorizeTenantRoleAdmin(tenantId, out var forbidden))
            return forbidden;

        try
        {
            var result = await _roles.RevokeRolesAsync(tenantId, req.UserId, req.RoleIds, ct);
            return Ok(result);
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    private bool TryAuthorizeTenantRoleAdmin(Guid routeTenantId, out ObjectResult forbidden)
    {
        forbidden = StatusCode(StatusCodes.Status403Forbidden, new { message = "Forbidden." });
        return IdentityApiAuthorization.TryAuthorizeTenantOperation(
            User,
            routeTenantId,
            _accessOptions.Value,
            _accessOptions.Value.TenantRoleManagementPermissionNames);
    }
}

public sealed class UpsertRoleRequest
{
    public string Name { get; set; } = string.Empty;
}

public sealed class UserRoleMutationRequest
{
    public Guid UserId { get; set; }
    public List<Guid> RoleIds { get; set; } = [];
}
