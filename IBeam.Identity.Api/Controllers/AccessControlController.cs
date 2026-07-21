using System.Security.Claims;
using IBeam.Identity.Api.Authorization;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using GrantResourceAccessRequest = IBeam.AccessControl.GrantResourceAccessRequest;
using IResourceAccessService = IBeam.AccessControl.IResourceAccessService;
using UpdateResourceAccessGrantRequest = IBeam.AccessControl.UpdateResourceAccessGrantRequest;

namespace IBeam.Identity.Api.Controllers;

[ApiController]
[Authorize]
public sealed class AccessControlController : ControllerBase
{
    private readonly IIBeamAccessControlService _access;
    private readonly IApiCredentialAccessService _apiCredentialAccess;
    private readonly IApiCredentialService _apiCredentials;
    private readonly IResourceAccessService _resourceAccess;
    private readonly IOptionsSnapshot<IBeamAccessControlOptions> _accessOptions;

    public AccessControlController(
        IIBeamAccessControlService access,
        IApiCredentialAccessService apiCredentialAccess,
        IApiCredentialService apiCredentials,
        IResourceAccessService resourceAccess,
        IOptionsSnapshot<IBeamAccessControlOptions> accessOptions)
    {
        _access = access;
        _apiCredentialAccess = apiCredentialAccess;
        _apiCredentials = apiCredentials;
        _resourceAccess = resourceAccess;
        _accessOptions = accessOptions;
    }

    [HttpGet("/api/access/me")]
    public async Task<IActionResult> GetCurrentAccess(CancellationToken ct)
    {
        if (string.Equals(User.FindFirstValue("api_subject_type"), "credential", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(User.FindFirstValue("api_credential_id")))
        {
            var credentialContext = await _apiCredentialAccess.GetCurrentAccessContextAsync(User, ResolveRequestedAgentKey(), ct);
            return Ok(credentialContext);
        }

        var userContext = await _access.GetCurrentAccessContextAsync(User, tenantId: null, ct);
        return Ok(userContext);
    }

    [HttpGet("/api/tenants/{tenantId:guid}/access-control/me")]
    public async Task<IActionResult> GetTenantCurrentAccess(Guid tenantId, CancellationToken ct)
    {
        if (!TryAuthorizeTenantMember(tenantId, out var forbidden))
            return forbidden;

        var context = await _access.GetCurrentAccessContextAsync(User, tenantId, ct);
        return Ok(context);
    }

    [HttpGet("/api/tenants/{tenantId:guid}/access-catalog")]
    public async Task<IActionResult> GetAccessCatalog(
        Guid tenantId,
        [FromQuery] string? subjectType,
        [FromQuery] string? category,
        CancellationToken ct)
    {
        if (!TryAuthorizeTenantRoleAdmin(tenantId, out var forbidden))
            return forbidden;

        var catalog = await _access.GetAccessCatalogAsync(tenantId, ct);
        catalog = FilterCatalog(catalog, subjectType, category);
        return Ok(catalog);
    }

    [HttpGet("/api/tenants/{tenantId:guid}/access-catalog/overrides")]
    public async Task<IActionResult> GetAccessCatalogOverrides(Guid tenantId, CancellationToken ct)
    {
        if (!TryAuthorizeTenantRoleAdmin(tenantId, out var forbidden))
            return forbidden;

        var overrides = await _access.GetAccessCatalogOverridesAsync(tenantId, ct);
        return Ok(overrides);
    }

    [HttpGet("/api/tenants/{tenantId:guid}/access-catalog/operations")]
    public async Task<IActionResult> GetOperationCatalog(
        Guid tenantId,
        [FromQuery] string? module,
        [FromQuery] string? resourceType,
        [FromQuery] bool? isDangerous,
        CancellationToken ct)
    {
        if (!TryAuthorizeTenantRoleAdmin(tenantId, out var forbidden))
            return forbidden;

        var operations = await _access.GetOperationCatalogAsync(tenantId, ct);
        operations = operations
            .Where(x => string.IsNullOrWhiteSpace(module) ||
                        string.Equals(x.ModuleKey, module.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(resourceType) ||
                        string.Equals(x.ResourceType, resourceType.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(x => isDangerous is null || x.IsDangerous == isDangerous.Value)
            .ToList();

        return Ok(operations);
    }

    [HttpPost("/api/tenants/{tenantId:guid}/access-catalog/overrides")]
    public async Task<IActionResult> CreateAccessCatalogOverride(
        Guid tenantId,
        [FromBody] UpsertAccessCatalogOverrideRequest req,
        CancellationToken ct)
    {
        if (!TryAuthorizeTenantRoleAdmin(tenantId, out var forbidden))
            return forbidden;

        try
        {
            var item = await _access.UpsertAccessCatalogOverrideAsync(tenantId, null, req, ct);
            return Ok(item);
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [HttpPut("/api/tenants/{tenantId:guid}/access-catalog/overrides/{catalogItemId:guid}")]
    public async Task<IActionResult> UpdateAccessCatalogOverride(
        Guid tenantId,
        Guid catalogItemId,
        [FromBody] UpsertAccessCatalogOverrideRequest req,
        CancellationToken ct)
    {
        if (!TryAuthorizeTenantRoleAdmin(tenantId, out var forbidden))
            return forbidden;

        try
        {
            var item = await _access.UpsertAccessCatalogOverrideAsync(tenantId, catalogItemId, req, ct);
            return Ok(item);
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [HttpDelete("/api/tenants/{tenantId:guid}/access-catalog/overrides/{catalogItemId:guid}")]
    public async Task<IActionResult> DeleteAccessCatalogOverride(Guid tenantId, Guid catalogItemId, CancellationToken ct)
    {
        if (!TryAuthorizeTenantRoleAdmin(tenantId, out var forbidden))
            return forbidden;

        await _access.DeleteAccessCatalogOverrideAsync(tenantId, catalogItemId, ct);
        return Accepted();
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

        var subject = !string.IsNullOrWhiteSpace(subjectType) || !string.IsNullOrWhiteSpace(subjectId)
            ? new IBeam.AccessControl.AccessSubject(subjectType ?? string.Empty, subjectId ?? string.Empty)
            : null;

        var grants = await _resourceAccess.ListGrantsAsync(tenantId, subject: subject, ct: ct);
        return Ok(grants);
    }

    [HttpPost("/api/tenants/{tenantId:guid}/access-control/grants")]
    public async Task<IActionResult> CreateGrant(Guid tenantId, [FromBody] GrantResourceAccessRequest req, CancellationToken ct)
    {
        if (!TryAuthorizeTenantRoleAdmin(tenantId, out var forbidden))
            return forbidden;

        try
        {
            var grant = await _resourceAccess.GrantAccessAsync(
                tenantId,
                req,
                ResolveUserId(User),
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
        [FromBody] UpdateResourceAccessGrantRequest req,
        CancellationToken ct)
    {
        if (!TryAuthorizeTenantRoleAdmin(tenantId, out var forbidden))
            return forbidden;

        try
        {
            var grant = await _resourceAccess.UpdateGrantAsync(tenantId, grantId, req, ct);

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

        await _resourceAccess.RevokeGrantAsync(tenantId, grantId, ct);
        return Accepted();
    }

    [HttpPost("/api/tenants/{tenantId:guid}/access-control/check")]
    public async Task<IActionResult> Check(Guid tenantId, [FromBody] AccessCheckRequest req, CancellationToken ct)
    {
        if (!TryAuthorizeTenantMember(tenantId, out var forbidden))
            return forbidden;

        if (string.Equals(req.SubjectType, AccessSubjectTypes.ApiCredential, StringComparison.OrdinalIgnoreCase))
        {
            var credentialDecision = await CheckApiCredentialAccessAsync(tenantId, req, ct);
            return Ok(credentialDecision);
        }

        var decision = await _access.CheckAccessAsync(User, tenantId, req, ct);
        return Ok(decision);
    }

    private async Task<AccessDecision> CheckApiCredentialAccessAsync(
        Guid tenantId,
        AccessCheckRequest req,
        CancellationToken ct)
    {
        if (!Guid.TryParse(req.SubjectId, out var credentialId) || credentialId == Guid.Empty)
            return new AccessDecision(false, "validation", "subjectId must be an API credential id.", req.AccessLevel);

        try
        {
            var context = await _apiCredentials.GetAccessAsync(tenantId, credentialId, req.AgentKey, ct);

            if (!string.IsNullOrWhiteSpace(req.AgentKey) &&
                !context.Capabilities.CanActAsRequestedAgent)
                return new AccessDecision(false, "agent", $"Credential cannot act as agent '{req.AgentKey}'.", EffectiveAccessLevel(req));

            if (!string.IsNullOrWhiteSpace(req.Module) &&
                !ContainsWildcardOrValue(context.ApiScopes, req.Module))
                return new AccessDecision(false, "api-scope", $"API scope '{req.Module}' is required.", EffectiveAccessLevel(req));

            if (!string.IsNullOrWhiteSpace(req.Permission) &&
                !context.Permissions.Contains(req.Permission.Trim(), StringComparer.OrdinalIgnoreCase))
                return new AccessDecision(false, "permission", $"Permission '{req.Permission}' is required.", EffectiveAccessLevel(req));

            if (!string.IsNullOrWhiteSpace(req.ResourceType) &&
                !string.IsNullOrWhiteSpace(req.ResourceId) &&
                !HasCredentialResourceAccess(context, req.ResourceType, req.ResourceId, EffectiveAccessLevel(req)))
                return new AccessDecision(false, "resource", null, EffectiveAccessLevel(req));

            return new AccessDecision(true, AccessSubjectTypes.ApiCredential, null, EffectiveAccessLevel(req));
        }
        catch (IdentityNotFoundException ex)
        {
            return new AccessDecision(false, "not_found", ex.Message, EffectiveAccessLevel(req));
        }
    }

    private bool TryAuthorizeTenantMember(Guid routeTenantId, out ObjectResult forbidden)
    {
        forbidden = StatusCode(StatusCodes.Status403Forbidden, new { message = "Forbidden." });
        return IdentityApiAuthorization.TryAuthorizeTenantMember(User, routeTenantId);
    }

    private bool TryAuthorizeTenantRoleAdmin(Guid routeTenantId, out ObjectResult forbidden)
    {
        forbidden = StatusCode(StatusCodes.Status403Forbidden, new { message = "Forbidden." });
        return IdentityApiAuthorization.TryAuthorizeTenantOperation(
            User,
            routeTenantId,
            _accessOptions.Value,
            _accessOptions.Value.TenantAccessControlManagementPermissionNames);
    }

    private string? ResolveRequestedAgentKey()
        => FirstNonEmpty(
            Request.Headers["X-Agent-Key"].FirstOrDefault(),
            Request.Headers["X-Api-Agent"].FirstOrDefault(),
            Request.Headers["X-Api-Agent-Key"].FirstOrDefault(),
            User.FindFirstValue("agent_key"),
            User.FindFirstValue("api_agent_key"),
            User.FindFirstValue("apiAgentKey"),
            User.FindFirstValue("apiAgentId"));

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue("uid") ??
                  user.FindFirstValue(ClaimTypes.NameIdentifier) ??
                  user.FindFirstValue("sub");

        return Guid.TryParse(raw, out var parsed) && parsed != Guid.Empty ? parsed : null;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();

    private static bool ContainsWildcardOrValue(IEnumerable<string> values, string value)
        => values.Any(x =>
            string.Equals(x, "*", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x, value.Trim(), StringComparison.OrdinalIgnoreCase));

    private static bool HasCredentialResourceAccess(
        ApiCredentialAccessContextDto context,
        string resourceType,
        string resourceId,
        string minimumAccessLevel)
    {
        return context.Resources.TryGetValue(resourceType.Trim(), out var entries) &&
               entries.Any(x =>
                   (string.Equals(x.ResourceId, resourceId.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(x.ResourceId, "*", StringComparison.OrdinalIgnoreCase)) &&
                   AccessRank(x.AccessLevel) >= AccessRank(minimumAccessLevel));
    }

    private static int AccessRank(string? accessLevel)
        => accessLevel?.Trim().ToLowerInvariant() switch
        {
            AccessLevels.Manage => 20,
            AccessLevels.Edit => 10,
            AccessLevels.View => 0,
            "*" => 100,
            _ => 0
        };

    private static AccessCatalogDto FilterCatalog(AccessCatalogDto catalog, string? subjectType, string? category)
    {
        var roles = FilterItems(catalog.Roles, subjectType, category);
        var permissions = FilterItems(catalog.Permissions, subjectType, category);
        var modules = FilterItems(catalog.Modules, subjectType, category);
        var apiScopes = FilterItems(catalog.ApiScopes, subjectType, category);
        var tools = FilterItems(catalog.Tools, subjectType, category);
        var agents = FilterItems(catalog.Agents, subjectType, category);
        var resources = FilterItems(catalog.Resources, subjectType, category);
        var accessLevels = FilterItems(catalog.AccessLevels, subjectType, category);

        return new AccessCatalogDto(
            roles,
            permissions,
            FilterItems(catalog.Operations, subjectType, category),
            modules,
            apiScopes,
            tools,
            agents,
            resources,
            accessLevels,
            resources
                .Select(x => x.ResourceType)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    private static string EffectiveAccessLevel(AccessCheckRequest request)
        => string.IsNullOrWhiteSpace(request.MinimumAccessLevel)
            ? request.AccessLevel
            : request.MinimumAccessLevel.Trim();

    private static IReadOnlyList<AccessCatalogItem> FilterItems(
        IReadOnlyList<AccessCatalogItem> items,
        string? subjectType,
        string? category)
        => items
            .Where(x => string.IsNullOrWhiteSpace(category) ||
                        string.Equals(x.Category, category.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(subjectType) ||
                        x.SubjectTypes is null ||
                        x.SubjectTypes.Count == 0 ||
                        x.SubjectTypes.Any(s => string.Equals(s, subjectType.Trim(), StringComparison.OrdinalIgnoreCase)))
            .ToList();
}
