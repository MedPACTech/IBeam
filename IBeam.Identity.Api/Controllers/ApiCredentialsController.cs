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

namespace IBeam.Identity.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/api-credentials")]
public sealed class ApiCredentialsController : ControllerBase
{
    private readonly IApiCredentialService _credentials;
    private readonly IApiCredentialAuthenticator _authenticator;
    private readonly IApiCredentialRoleCatalogProvider _roleCatalog;
    private readonly IApiCredentialScopeCatalogProvider _scopeCatalog;
    private readonly IApiCredentialAccessService _access;
    private readonly IOptionsSnapshot<IBeamAccessControlOptions> _accessOptions;

    public ApiCredentialsController(
        IApiCredentialService credentials,
        IApiCredentialAuthenticator authenticator,
        IApiCredentialRoleCatalogProvider roleCatalog,
        IApiCredentialScopeCatalogProvider scopeCatalog,
        IApiCredentialAccessService access,
        IOptionsSnapshot<IBeamAccessControlOptions> accessOptions)
    {
        _credentials = credentials;
        _authenticator = authenticator;
        _roleCatalog = roleCatalog;
        _scopeCatalog = scopeCatalog;
        _access = access;
        _accessOptions = accessOptions;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!TryAuthorizeHumanTenantAdmin(out var tenantId, out _, out var forbidden))
            return forbidden;

        var result = await _credentials.ListAsync(tenantId, ct);
        return Ok(result);
    }

    [HttpGet("/api/tenants/{tenantId:guid}/api-credentials")]
    public async Task<IActionResult> ListForTenant(Guid tenantId, CancellationToken ct)
    {
        if (!TryAuthorizeHumanTenantAdmin(tenantId, out _, out var forbidden))
            return forbidden;

        var result = await _credentials.ListAsync(tenantId, ct);
        return Ok(result);
    }

    [HttpGet("/api/tenants/{tenantId:guid}/api-credentials/{credentialId:guid}")]
    public async Task<IActionResult> GetForTenant(Guid tenantId, Guid credentialId, CancellationToken ct)
    {
        if (!TryAuthorizeHumanTenantAdmin(tenantId, out _, out var forbidden))
            return forbidden;

        try
        {
            var result = await _credentials.GetAsync(tenantId, credentialId, ct);
            return Ok(result);
        }
        catch (IdentityNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("role-catalog")]
    public async Task<IActionResult> RoleCatalog(CancellationToken ct)
    {
        if (!TryAuthorizeHumanTenantAdmin(out _, out _, out var forbidden))
            return forbidden;

        var result = await _roleCatalog.ListAsync(ct).ConfigureAwait(false);
        return Ok(result);
    }

    [HttpGet("/api/tenants/{tenantId:guid}/api-credentials/scope-catalog")]
    public async Task<IActionResult> ScopeCatalog(Guid tenantId, CancellationToken ct)
    {
        if (!TryAuthorizeHumanTenantAdmin(tenantId, out _, out var forbidden))
            return forbidden;

        var result = await _scopeCatalog.GetScopesAsync(tenantId, ct).ConfigureAwait(false);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateApiCredentialRequest request, CancellationToken ct)
    {
        if (!TryAuthorizeHumanTenantAdmin(out var tenantId, out var userId, out var forbidden))
            return forbidden;

        try
        {
            var result = await _credentials.CreateAsync(tenantId, request, userId, ct);
            return Ok(result);
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [HttpPost("/api/tenants/{tenantId:guid}/api-credentials")]
    public async Task<IActionResult> CreateForTenant(Guid tenantId, [FromBody] CreateApiCredentialRequest request, CancellationToken ct)
    {
        if (!TryAuthorizeHumanTenantAdmin(tenantId, out var userId, out var forbidden))
            return forbidden;

        try
        {
            var result = await _credentials.CreateAsync(tenantId, request, userId, ct);
            return Ok(result);
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [HttpPut("/api/tenants/{tenantId:guid}/api-credentials/{credentialId:guid}")]
    public async Task<IActionResult> UpdateForTenant(
        Guid tenantId,
        Guid credentialId,
        [FromBody] UpdateApiCredentialRequest request,
        CancellationToken ct)
    {
        if (!TryAuthorizeHumanTenantAdmin(tenantId, out _, out var forbidden))
            return forbidden;

        try
        {
            var result = await _credentials.UpdateAsync(tenantId, credentialId, request, ct);
            return Ok(result);
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

    [HttpPut("{credentialId:guid}/roles")]
    public async Task<IActionResult> UpdateRoles(
        Guid credentialId,
        [FromBody] UpdateApiCredentialRolesRequest request,
        CancellationToken ct)
    {
        if (!TryAuthorizeHumanTenantAdmin(out var tenantId, out _, out var forbidden))
            return forbidden;

        try
        {
            var result = await _credentials.UpdateRolesAsync(tenantId, credentialId, request, ct);
            return Ok(result);
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

    [HttpGet("/api/tenants/{tenantId:guid}/api-credentials/{credentialId:guid}/roles")]
    public async Task<IActionResult> GetRolesForTenant(Guid tenantId, Guid credentialId, CancellationToken ct)
    {
        if (!TryAuthorizeHumanTenantAdmin(tenantId, out _, out var forbidden))
            return forbidden;

        try
        {
            var result = await _credentials.GetAsync(tenantId, credentialId, ct);
            return Ok(new UpdateApiCredentialRolesRequest
            {
                RoleNames = result.RoleNames.ToList(),
                RoleIds = result.RoleIds.ToList()
            });
        }
        catch (IdentityNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPut("/api/tenants/{tenantId:guid}/api-credentials/{credentialId:guid}/roles")]
    public Task<IActionResult> UpdateRolesForTenant(
        Guid tenantId,
        Guid credentialId,
        [FromBody] UpdateApiCredentialRolesRequest request,
        CancellationToken ct)
        => UpdateRolesCore(tenantId, credentialId, request, ct);

    [HttpGet("/api/tenants/{tenantId:guid}/api-credentials/{credentialId:guid}/access")]
    public async Task<IActionResult> GetAccessForTenant(Guid tenantId, Guid credentialId, CancellationToken ct)
    {
        if (!TryAuthorizeHumanTenantAdmin(tenantId, out _, out var forbidden))
            return forbidden;

        try
        {
            var result = await _credentials.GetAccessAsync(tenantId, credentialId, ResolveRequestedAgentKey(), ct);
            return Ok(result);
        }
        catch (IdentityNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPut("/api/tenants/{tenantId:guid}/api-credentials/{credentialId:guid}/access")]
    public async Task<IActionResult> UpdateAccessForTenant(
        Guid tenantId,
        Guid credentialId,
        [FromBody] UpdateApiCredentialAccessRequest request,
        CancellationToken ct)
    {
        if (!TryAuthorizeHumanTenantAdmin(tenantId, out _, out var forbidden))
            return forbidden;

        try
        {
            var result = await _credentials.UpdateAccessAsync(tenantId, credentialId, request, ct);
            return Ok(result);
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

    [HttpGet("me/access")]
    public async Task<IActionResult> CurrentCredentialAccess(CancellationToken ct)
    {
        try
        {
            var result = await _access.GetCurrentAccessContextAsync(User, ResolveRequestedAgentKey(), ct);
            return Ok(result);
        }
        catch (IdentityUnauthorizedException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [HttpPost("{credentialId:guid}/revoke")]
    public async Task<IActionResult> Revoke(
        Guid credentialId,
        [FromBody] RevokeApiCredentialRequest request,
        CancellationToken ct)
    {
        if (!TryAuthorizeHumanTenantAdmin(out var tenantId, out var userId, out var forbidden))
            return forbidden;

        try
        {
            var result = await _credentials.RevokeAsync(tenantId, credentialId, userId, request?.Reason, ct);
            return Ok(result);
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

    [HttpPost("/api/tenants/{tenantId:guid}/api-credentials/{credentialId:guid}/rotate")]
    public async Task<IActionResult> RotateForTenant(Guid tenantId, Guid credentialId, CancellationToken ct)
    {
        if (!TryAuthorizeHumanTenantAdmin(tenantId, out _, out var forbidden))
            return forbidden;

        try
        {
            var result = await _credentials.RotateAsync(tenantId, credentialId, ct);
            return Ok(result);
        }
        catch (IdentityNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("/api/tenants/{tenantId:guid}/api-credentials/{credentialId:guid}/revoke")]
    public Task<IActionResult> RevokeForTenant(
        Guid tenantId,
        Guid credentialId,
        [FromBody] RevokeApiCredentialRequest request,
        CancellationToken ct)
        => RevokeCore(tenantId, credentialId, request, ct);

    [HttpDelete("/api/tenants/{tenantId:guid}/api-credentials/{credentialId:guid}")]
    public Task<IActionResult> DeleteForTenant(
        Guid tenantId,
        Guid credentialId,
        CancellationToken ct)
        => RevokeCore(tenantId, credentialId, new RevokeApiCredentialRequest { Reason = "deleted" }, ct);

    [HttpPost("/api/tenants/{tenantId:guid}/api-credentials/{credentialId:guid}/activate")]
    public async Task<IActionResult> ActivateForTenant(Guid tenantId, Guid credentialId, CancellationToken ct)
    {
        if (!TryAuthorizeHumanTenantAdmin(tenantId, out _, out var forbidden))
            return forbidden;

        try
        {
            var result = await _credentials.ActivateAsync(tenantId, credentialId, ct);
            return Ok(result);
        }
        catch (IdentityNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("introspect")]
    public async Task<IActionResult> Introspect([FromBody] ApiCredentialIntrospectionRequest request, CancellationToken ct)
    {
        if (!TryAuthorizeHumanTenantAdmin(out var callerTenantId, out _, out var forbidden))
            return forbidden;

        if (request is null || string.IsNullOrWhiteSpace(request.ApiKey))
            return BadRequest(new { message = "apiKey is required." });

        var auth = await _authenticator.AuthenticateAsync(request.ApiKey, HttpContext.Connection.RemoteIpAddress?.ToString(), ct)
            .ConfigureAwait(false);

        if (!auth.Succeeded || auth.Credential is null)
        {
            return Ok(new ApiCredentialIntrospectionResult
            {
                Active = false,
                FailureReason = auth.FailureReason
            });
        }

        if (auth.Credential.TenantId != callerTenantId)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Forbidden." });

        return Ok(new ApiCredentialIntrospectionResult
        {
            Active = true,
            TenantId = auth.Credential.TenantId,
            CredentialId = auth.Credential.CredentialId,
            DisplayName = auth.Credential.DisplayName,
            AgentKey = auth.Credential.AgentKey,
            RoleNames = auth.Credential.RoleNames,
            RoleIds = auth.Credential.RoleIds,
            ExpiresUtc = auth.Credential.ExpiresUtc,
            ApiSubjectType = "credential"
        });
    }

    private bool TryAuthorizeHumanTenantAdmin(out Guid tenantId, out Guid? userId, out ObjectResult forbidden)
    {
        forbidden = StatusCode(StatusCodes.Status403Forbidden, new { message = "Forbidden." });
        return IdentityApiAuthorization.TryAuthorizeHumanTenantOperation(
            User,
            _accessOptions.Value,
            _accessOptions.Value.ApiCredentialManagementPermissionNames,
            out tenantId,
            out userId);
    }

    private bool TryAuthorizeHumanTenantAdmin(Guid routeTenantId, out Guid? userId, out ObjectResult forbidden)
    {
        forbidden = StatusCode(StatusCodes.Status403Forbidden, new { message = "Forbidden." });
        return IdentityApiAuthorization.TryAuthorizeHumanTenantOperation(
            User,
            routeTenantId,
            _accessOptions.Value,
            _accessOptions.Value.ApiCredentialManagementPermissionNames,
            out userId);
    }

    private async Task<IActionResult> UpdateRolesCore(
        Guid tenantId,
        Guid credentialId,
        UpdateApiCredentialRolesRequest request,
        CancellationToken ct)
    {
        if (!TryAuthorizeHumanTenantAdmin(tenantId, out _, out var forbidden))
            return forbidden;

        try
        {
            var result = await _credentials.UpdateRolesAsync(tenantId, credentialId, request, ct);
            return Ok(result);
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

    private async Task<IActionResult> RevokeCore(
        Guid tenantId,
        Guid credentialId,
        RevokeApiCredentialRequest request,
        CancellationToken ct)
    {
        if (!TryAuthorizeHumanTenantAdmin(tenantId, out var userId, out var forbidden))
            return forbidden;

        try
        {
            var result = await _credentials.RevokeAsync(tenantId, credentialId, userId, request?.Reason, ct);
            return Ok(result);
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

    private string? ResolveRequestedAgentKey()
        => FirstNonEmpty(
            Request.Headers["X-Agent-Key"].FirstOrDefault(),
            Request.Headers["X-Api-Agent"].FirstOrDefault(),
            Request.Headers["X-Api-Agent-Key"].FirstOrDefault(),
            User.FindFirstValue("agent_key"),
            User.FindFirstValue("api_agent_key"),
            User.FindFirstValue("apiAgentKey"),
            User.FindFirstValue("apiAgentId"));

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();

}
