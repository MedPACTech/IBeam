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
[Route("api/api-credentials")]
public sealed class ApiCredentialsController : ControllerBase
{
    private static readonly string[] ManageRoleClaims = ["owner", "administrator", "admin"];

    private readonly IApiCredentialService _credentials;
    private readonly IApiCredentialAuthenticator _authenticator;

    public ApiCredentialsController(
        IApiCredentialService credentials,
        IApiCredentialAuthenticator authenticator)
    {
        _credentials = credentials;
        _authenticator = authenticator;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!TryAuthorizeHumanTenantAdmin(out var tenantId, out _, out var forbidden))
            return forbidden;

        var result = await _credentials.ListAsync(tenantId, ct);
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
        tenantId = Guid.Empty;
        userId = null;
        forbidden = StatusCode(StatusCodes.Status403Forbidden, new { message = "Forbidden." });

        if (string.Equals(User.FindFirstValue("api_subject_type"), "credential", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!Guid.TryParse(User.FindFirstValue("tid"), out tenantId))
            return false;

        if (Guid.TryParse(User.FindFirstValue("uid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var parsedUserId))
            userId = parsedUserId;

        var roleClaims = User.FindAll("role")
            .Concat(User.FindAll(ClaimTypes.Role))
            .Select(x => x.Value)
            .ToList();

        return roleClaims.Any(x => ManageRoleClaims.Contains(x, StringComparer.OrdinalIgnoreCase));
    }
}
