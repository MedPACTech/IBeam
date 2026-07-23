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
public sealed class AgentUsersController : ControllerBase
{
    private readonly IAgentUserService _agentUsers;
    private readonly IOptionsSnapshot<IBeamAccessControlOptions> _accessOptions;

    public AgentUsersController(
        IAgentUserService agentUsers,
        IOptionsSnapshot<IBeamAccessControlOptions> accessOptions)
    {
        _agentUsers = agentUsers ?? throw new ArgumentNullException(nameof(agentUsers));
        _accessOptions = accessOptions ?? throw new ArgumentNullException(nameof(accessOptions));
    }

    [HttpGet("agentusers/me")]
    public async Task<IActionResult> GetCurrentAgentUser(CancellationToken ct)
    {
        try
        {
            return Ok(await _agentUsers.GetCurrentAsync(User, ct).ConfigureAwait(false));
        }
        catch (IdentityUnauthorizedException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    [HttpGet("tenants/{tenantId:guid}/agentusers")]
    public async Task<IActionResult> List(Guid tenantId, CancellationToken ct)
    {
        if (!TryAuthorizeTenantAdmin(tenantId, out var forbidden))
            return forbidden;

        return Ok(await _agentUsers.ListAsync(tenantId, ct).ConfigureAwait(false));
    }

    [HttpPost("tenants/{tenantId:guid}/agentusers")]
    public async Task<IActionResult> Create(Guid tenantId, [FromBody] CreateAgentUserRequest request, CancellationToken ct)
    {
        if (!TryAuthorizeTenantAdmin(tenantId, out var forbidden))
            return forbidden;

        var createdBy = TryGetCurrentUserId(out var userId) ? userId : (Guid?)null;
        try
        {
            return Ok(await _agentUsers.CreateAsync(tenantId, request, createdBy, ct).ConfigureAwait(false));
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { message = ex.Message, errors = ex.Errors });
        }
    }

    [HttpGet("tenants/{tenantId:guid}/agentusers/{agentUserId:guid}")]
    public async Task<IActionResult> Get(Guid tenantId, Guid agentUserId, CancellationToken ct)
    {
        if (!TryAuthorizeTenantAdmin(tenantId, out var forbidden))
            return forbidden;

        try
        {
            return Ok(await _agentUsers.GetAsync(tenantId, agentUserId, ct).ConfigureAwait(false));
        }
        catch (IdentityNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPut("tenants/{tenantId:guid}/agentusers/{agentUserId:guid}")]
    public async Task<IActionResult> Update(
        Guid tenantId,
        Guid agentUserId,
        [FromBody] UpdateAgentUserRequest request,
        CancellationToken ct)
    {
        if (!TryAuthorizeTenantAdmin(tenantId, out var forbidden))
            return forbidden;

        try
        {
            return Ok(await _agentUsers.UpdateAsync(tenantId, agentUserId, request, ct).ConfigureAwait(false));
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

    [HttpGet("tenants/{tenantId:guid}/agentusers/{agentUserId:guid}/credentials")]
    public async Task<IActionResult> ListCredentials(Guid tenantId, Guid agentUserId, CancellationToken ct)
    {
        if (!TryAuthorizeTenantAdmin(tenantId, out var forbidden))
            return forbidden;

        return Ok(await _agentUsers.ListCredentialBindingsAsync(tenantId, agentUserId, ct).ConfigureAwait(false));
    }

    [HttpPost("tenants/{tenantId:guid}/agentusers/{agentUserId:guid}/credentials")]
    public async Task<IActionResult> BindCredential(
        Guid tenantId,
        Guid agentUserId,
        [FromBody] BindAgentUserCredentialRequest request,
        CancellationToken ct)
    {
        if (!TryAuthorizeTenantAdmin(tenantId, out var forbidden))
            return forbidden;

        var createdBy = TryGetCurrentUserId(out var userId) ? userId : (Guid?)null;
        try
        {
            return Ok(await _agentUsers.BindCredentialAsync(tenantId, agentUserId, request, createdBy, ct)
                .ConfigureAwait(false));
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

    [HttpDelete("tenants/{tenantId:guid}/agentusers/{agentUserId:guid}/credentials/{credentialId:guid}")]
    public async Task<IActionResult> RevokeCredential(
        Guid tenantId,
        Guid agentUserId,
        Guid credentialId,
        CancellationToken ct)
    {
        if (!TryAuthorizeTenantAdmin(tenantId, out var forbidden))
            return forbidden;

        var revokedBy = TryGetCurrentUserId(out var userId) ? userId : (Guid?)null;
        await _agentUsers.RevokeCredentialBindingAsync(tenantId, agentUserId, credentialId, revokedBy, ct)
            .ConfigureAwait(false);
        return Accepted();
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
