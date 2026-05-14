using System.Security.Claims;
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
[Route("api/tenants/{tenantId:guid}/permissions")]
public sealed class PermissionMappingsController : ControllerBase
{
    private static readonly string[] ManageRoleClaims = ["owner", "administrator", "admin"];

    private readonly IPermissionAccessStore _store;
    private readonly IPermissionCatalogProvider _catalog;
    private readonly IOptionsSnapshot<RoleManagementOptions> _roleOptions;
    private readonly IOptionsSnapshot<PermissionAccessOptions> _permissionOptions;

    public PermissionMappingsController(
        IPermissionAccessStore store,
        IPermissionCatalogProvider catalog,
        IOptionsSnapshot<RoleManagementOptions> roleOptions,
        IOptionsSnapshot<PermissionAccessOptions> permissionOptions)
    {
        _store = store;
        _catalog = catalog;
        _roleOptions = roleOptions;
        _permissionOptions = permissionOptions;
    }

    [HttpGet("catalog")]
    public async Task<IActionResult> GetCatalog(Guid tenantId, CancellationToken ct)
    {
        if (!TryAuthorizeTenantRoleAdmin(tenantId, out var forbidden))
            return forbidden;

        var catalog = await _catalog.GetExposedPermissionsAsync(ct);
        return Ok(catalog);
    }

    [HttpGet("mappings")]
    public async Task<IActionResult> GetMappings(Guid tenantId, CancellationToken ct)
    {
        if (!TryAuthorizeTenantRoleAdmin(tenantId, out var forbidden))
            return forbidden;

        var mode = _roleOptions.Value.PermissionMode;

        var repositoryMappings = IsRepositoryEnabled(mode)
            ? await _store.GetMappingsAsync(tenantId, ct)
            : Array.Empty<PermissionRoleMap>();

        var configurationMappings = _permissionOptions.Value.Mappings
            .Where(x => x is not null)
            .Where(x => !x.TenantId.HasValue || x.TenantId.Value == tenantId)
            .Select(x => new PermissionMappingView
            {
                Source = "configuration",
                TenantId = x.TenantId ?? tenantId,
                PermissionName = x.PermissionName,
                PermissionId = x.PermissionId,
                RoleNames = x.RoleNames,
                RoleIds = x.RoleIds
            })
            .ToList();

        var repositoryViews = repositoryMappings
            .Select(x => new PermissionMappingView
            {
                Source = "repository",
                TenantId = x.TenantId,
                PermissionName = x.PermissionName,
                PermissionId = x.PermissionId,
                RoleNames = x.RoleNames.ToList(),
                RoleIds = x.RoleIds.ToList(),
                UpdatedAt = x.UpdatedAt,
                IsActive = x.IsActive
            })
            .ToList();

        return Ok(new PermissionMappingsResponse
        {
            Mode = mode.ToString(),
            Repository = repositoryViews,
            Configuration = configurationMappings
        });
    }

    [HttpPut("mappings/by-name")]
    public async Task<IActionResult> UpsertByName(Guid tenantId, [FromBody] PermissionMapByNameRequest req, CancellationToken ct)
    {
        if (!TryAuthorizeTenantRoleAdmin(tenantId, out var forbidden))
            return forbidden;

        if (TryRejectMutation(out var rejection))
            return rejection;

        var map = await _store.UpsertByPermissionNameAsync(
            tenantId,
            req.PermissionName,
            req.RoleNames,
            req.RoleIds,
            ct);

        return Ok(map);
    }

    [HttpPut("mappings/by-id")]
    public async Task<IActionResult> UpsertById(Guid tenantId, [FromBody] PermissionMapByIdRequest req, CancellationToken ct)
    {
        if (!TryAuthorizeTenantRoleAdmin(tenantId, out var forbidden))
            return forbidden;

        if (TryRejectMutation(out var rejection))
            return rejection;

        var map = await _store.UpsertByPermissionIdAsync(
            tenantId,
            req.PermissionId,
            req.RoleNames,
            req.RoleIds,
            ct);

        return Ok(map);
    }

    [HttpDelete("mappings/by-name")]
    public async Task<IActionResult> DeleteByName(Guid tenantId, [FromQuery] string permissionName, CancellationToken ct)
    {
        if (!TryAuthorizeTenantRoleAdmin(tenantId, out var forbidden))
            return forbidden;

        if (TryRejectMutation(out var rejection))
            return rejection;

        await _store.DeleteByPermissionNameAsync(tenantId, permissionName, ct);
        return Accepted();
    }

    [HttpDelete("mappings/by-id/{permissionId:guid}")]
    public async Task<IActionResult> DeleteById(Guid tenantId, Guid permissionId, CancellationToken ct)
    {
        if (!TryAuthorizeTenantRoleAdmin(tenantId, out var forbidden))
            return forbidden;

        if (TryRejectMutation(out var rejection))
            return rejection;

        await _store.DeleteByPermissionIdAsync(tenantId, permissionId, ct);
        return Accepted();
    }

    private bool TryRejectMutation(out ObjectResult rejection)
    {
        rejection = StatusCode(StatusCodes.Status403Forbidden, new { message = "Tenant role mapping mutation is disabled." });

        var opts = _roleOptions.Value;
        if (!opts.AllowTenantPermissionMapMutation)
            return true;

        if (!IsRepositoryEnabled(opts.PermissionMode))
        {
            rejection = StatusCode(
                StatusCodes.Status409Conflict,
                new { message = "Permission map mutation requires repository-backed mode." });
            return true;
        }

        return false;
    }

    private static bool IsRepositoryEnabled(PermissionManagementMode mode)
        => mode is PermissionManagementMode.Repository or PermissionManagementMode.Hybrid;

    private bool TryAuthorizeTenantRoleAdmin(Guid routeTenantId, out ObjectResult forbidden)
    {
        forbidden = StatusCode(StatusCodes.Status403Forbidden, new { message = "Forbidden." });

        var claimTenant = User.FindFirstValue("tid");
        if (!Guid.TryParse(claimTenant, out var tokenTenantId))
            return false;

        if (tokenTenantId != routeTenantId)
            return false;

        var roleClaims = User.FindAll("role").Select(x => x.Value).ToList();
        if (roleClaims.Any(x => ManageRoleClaims.Contains(x, StringComparer.OrdinalIgnoreCase)))
            return true;

        return false;
    }
}

public sealed class PermissionMapByNameRequest
{
    public string PermissionName { get; set; } = string.Empty;
    public List<string> RoleNames { get; set; } = [];
    public List<Guid> RoleIds { get; set; } = [];
}

public sealed class PermissionMapByIdRequest
{
    public Guid PermissionId { get; set; }
    public List<string> RoleNames { get; set; } = [];
    public List<Guid> RoleIds { get; set; } = [];
}

public sealed class PermissionMappingsResponse
{
    public string Mode { get; set; } = string.Empty;
    public List<PermissionMappingView> Repository { get; set; } = [];
    public List<PermissionMappingView> Configuration { get; set; } = [];
}

public sealed class PermissionMappingView
{
    public string Source { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public string? PermissionName { get; set; }
    public Guid? PermissionId { get; set; }
    public List<string> RoleNames { get; set; } = [];
    public List<Guid> RoleIds { get; set; } = [];
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? UpdatedAt { get; set; }
}
