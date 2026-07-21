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
[Route("api/tenants")]
public sealed class TenantsController : ControllerBase
{
    private readonly IIdentityTenantService _tenants;
    private readonly ITenantRoleService _roles;
    private readonly IOptionsSnapshot<IBeamAccessControlOptions> _accessOptions;

    public TenantsController(
        IIdentityTenantService tenants,
        ITenantRoleService roles,
        IOptionsSnapshot<IBeamAccessControlOptions> accessOptions)
    {
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _roles = roles ?? throw new ArgumentNullException(nameof(roles));
        _accessOptions = accessOptions ?? throw new ArgumentNullException(nameof(accessOptions));
    }

    [HttpGet("{tenantId:guid}")]
    public async Task<IActionResult> GetTenant(Guid tenantId, CancellationToken ct)
    {
        if (!TryAuthorizeTenantAdmin(tenantId, out var forbidden))
            return forbidden;

        var tenant = await _tenants.FindByIdAsync(tenantId, ct).ConfigureAwait(false);
        return tenant is null ? NotFound(new { message = "Tenant not found." }) : Ok(tenant);
    }

    [HttpPost]
    public async Task<IActionResult> CreateTenant([FromBody] CreateIdentityTenantRequest request, CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized(new { message = "Authenticated user id claim is missing." });

        try
        {
            var tenant = await _tenants.CreateAsync(request.Name, request.TenantId, ct: ct).ConfigureAwait(false);

            if (request.LinkCurrentUser)
            {
                await _roles.EnsureTenantMembershipAndGrantRolesAsync(
                    new TenantMembershipRoleBootstrapRequest(
                        tenant.TenantId,
                        userId,
                        TenantName: tenant.Name,
                        RoleNames: request.CurrentUserRoleNames.Count == 0 ? ["Owner"] : request.CurrentUserRoleNames,
                        SetAsDefault: request.SetAsDefault),
                    ct).ConfigureAwait(false);
            }

            return Ok(tenant);
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [HttpPut("{tenantId:guid}")]
    public async Task<IActionResult> UpdateTenant(Guid tenantId, [FromBody] UpdateIdentityTenantRequest request, CancellationToken ct)
    {
        if (!TryAuthorizeTenantAdmin(tenantId, out var forbidden))
            return forbidden;

        try
        {
            var existing = await _tenants.FindByIdAsync(tenantId, ct).ConfigureAwait(false);
            if (existing is null)
                return NotFound(new { message = "Tenant not found." });

            var updated = await _tenants.UpdateAsync(
                existing with
                {
                    Name = request.Name,
                    NormalizedName = IdentityTenant.NormalizeName(request.Name)
                },
                ct: ct).ConfigureAwait(false);

            return Ok(updated);
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [HttpPost("{tenantId:guid}/activate")]
    public async Task<IActionResult> ActivateTenant(Guid tenantId, CancellationToken ct)
    {
        if (!TryAuthorizeTenantAdmin(tenantId, out var forbidden))
            return forbidden;

        try
        {
            return Ok(await _tenants.ActivateAsync(tenantId, ct: ct).ConfigureAwait(false));
        }
        catch (IdentityNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("{tenantId:guid}/deactivate")]
    public async Task<IActionResult> DeactivateTenant(Guid tenantId, CancellationToken ct)
    {
        if (!TryAuthorizeTenantAdmin(tenantId, out var forbidden))
            return forbidden;

        try
        {
            return Ok(await _tenants.DeactivateAsync(tenantId, ct: ct).ConfigureAwait(false));
        }
        catch (IdentityNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("{tenantId:guid}/ensure-extension")]
    public async Task<IActionResult> EnsureTenantExtension(Guid tenantId, CancellationToken ct)
    {
        if (!TryAuthorizeTenantAdmin(tenantId, out var forbidden))
            return forbidden;

        try
        {
            await _tenants.EnsureExtensionAsync(tenantId, ct: ct).ConfigureAwait(false);
            return Accepted();
        }
        catch (IdentityNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    private bool TryAuthorizeTenantAdmin(Guid routeTenantId, out ObjectResult forbidden)
    {
        forbidden = StatusCode(StatusCodes.Status403Forbidden, new { message = "Forbidden." });
        return IdentityApiAuthorization.TryAuthorizeTenantOperation(
            User,
            routeTenantId,
            _accessOptions.Value,
            _accessOptions.Value.TenantManagementPermissionNames);
    }

    private bool TryGetCurrentUserId(out Guid userId)
        => IdentityApiAuthorization.TryGetCurrentUserId(User, out userId);
}

public sealed class CreateIdentityTenantRequest
{
    public string Name { get; set; } = string.Empty;
    public Guid? TenantId { get; set; }
    public bool LinkCurrentUser { get; set; } = true;
    public bool SetAsDefault { get; set; } = true;
    public List<string> CurrentUserRoleNames { get; set; } = ["Owner"];
}

public sealed class UpdateIdentityTenantRequest
{
    public string Name { get; set; } = string.Empty;
}
