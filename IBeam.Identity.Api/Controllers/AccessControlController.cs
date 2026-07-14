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
public sealed class AccessControlController : ControllerBase
{
    private static readonly string[] ManageRoleClaims = ["owner", "administrator", "admin"];

    private readonly IIBeamAccessControlService _access;
    private readonly IIBeamAccessGrantStore _grants;

    public AccessControlController(
        IIBeamAccessControlService access,
        IIBeamAccessGrantStore grants)
    {
        _access = access;
        _grants = grants;
    }

    [HttpGet("/api/access/me")]
    public Task<AccessContextDto> GetCurrentAccess(CancellationToken ct)
        => _access.GetCurrentAccessContextAsync(User, tenantId: null, ct);

    [HttpGet("/api/tenants/{tenantId:guid}/access-control/me")]
    public async Task<IActionResult> GetTenantCurrentAccess(Guid tenantId, CancellationToken ct)
    {
        if (!TryAuthorizeTenantMember(tenantId, out var forbidden))
            return forbidden;

        var context = await _access.GetCurrentAccessContextAsync(User, tenantId, ct);
        return Ok(context);
    }

    [HttpGet("/api/tenants/{tenantId:guid}/access-catalog")]
    public async Task<IActionResult> GetAccessCatalog(Guid tenantId, CancellationToken ct)
    {
        if (!TryAuthorizeTenantRoleAdmin(tenantId, out var forbidden))
            return forbidden;

        var catalog = await _access.GetAccessCatalogAsync(tenantId, ct);
        return Ok(catalog);
    }

    [HttpGet("/api/tenants/{tenantId:guid}/access-control/grants")]
    public async Task<IActionResult> GetGrants(
        Guid tenantId,
        [FromQuery] string? subjectType,
        [FromQuery] string? subjectId,
        CancellationToken ct)
    {
        if (!TryAuthorizeTenantRoleAdmin(tenantId, out var forbidden))
            return forbidden;

        var grants = await _grants.GetGrantsAsync(tenantId, subjectType, subjectId, ct);
        return Ok(grants);
    }

    [HttpPost("/api/tenants/{tenantId:guid}/access-control/grants")]
    public async Task<IActionResult> CreateGrant(Guid tenantId, [FromBody] UpsertAccessGrantRequest req, CancellationToken ct)
    {
        if (!TryAuthorizeTenantRoleAdmin(tenantId, out var forbidden))
            return forbidden;

        try
        {
            var grant = await _grants.UpsertGrantAsync(
                tenantId,
                grantId: null,
                req.SubjectType,
                req.SubjectId,
                req.ResourceType,
                req.ResourceId,
                req.AccessLevel,
                ct);

            return Ok(grant);
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [HttpPut("/api/tenants/{tenantId:guid}/access-control/grants/{grantId:guid}")]
    public async Task<IActionResult> UpdateGrant(
        Guid tenantId,
        Guid grantId,
        [FromBody] UpsertAccessGrantRequest req,
        CancellationToken ct)
    {
        if (!TryAuthorizeTenantRoleAdmin(tenantId, out var forbidden))
            return forbidden;

        try
        {
            var grant = await _grants.UpsertGrantAsync(
                tenantId,
                grantId,
                req.SubjectType,
                req.SubjectId,
                req.ResourceType,
                req.ResourceId,
                req.AccessLevel,
                ct);

            return Ok(grant);
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [HttpDelete("/api/tenants/{tenantId:guid}/access-control/grants/{grantId:guid}")]
    public async Task<IActionResult> DeleteGrant(Guid tenantId, Guid grantId, CancellationToken ct)
    {
        if (!TryAuthorizeTenantRoleAdmin(tenantId, out var forbidden))
            return forbidden;

        await _grants.DeleteGrantAsync(tenantId, grantId, ct);
        return Accepted();
    }

    [HttpPost("/api/tenants/{tenantId:guid}/access-control/check")]
    public async Task<IActionResult> Check(Guid tenantId, [FromBody] AccessCheckRequest req, CancellationToken ct)
    {
        if (!TryAuthorizeTenantMember(tenantId, out var forbidden))
            return forbidden;

        var decision = await _access.CheckAccessAsync(User, tenantId, req, ct);
        return Ok(decision);
    }

    private bool TryAuthorizeTenantMember(Guid routeTenantId, out ObjectResult forbidden)
    {
        forbidden = StatusCode(StatusCodes.Status403Forbidden, new { message = "Forbidden." });

        var claimTenant = User.FindFirstValue("tid");
        if (!Guid.TryParse(claimTenant, out var tokenTenantId))
            return false;

        return tokenTenantId == routeTenantId;
    }

    private bool TryAuthorizeTenantRoleAdmin(Guid routeTenantId, out ObjectResult forbidden)
    {
        if (!TryAuthorizeTenantMember(routeTenantId, out forbidden))
            return false;

        var roleClaims = User.FindAll("role").Select(x => x.Value).ToList();
        return roleClaims.Any(x => ManageRoleClaims.Contains(x, StringComparer.OrdinalIgnoreCase));
    }
}

public sealed class UpsertAccessGrantRequest
{
    public string SubjectType { get; set; } = AccessSubjectTypes.User;
    public string SubjectId { get; set; } = string.Empty;
    public string ResourceType { get; set; } = AccessResourceTypes.Module;
    public string ResourceId { get; set; } = string.Empty;
    public string AccessLevel { get; set; } = AccessLevels.View;
}

