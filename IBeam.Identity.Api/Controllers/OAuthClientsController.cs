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
[Route("api/tenants/{tenantId:guid}/oauth-clients")]
public sealed class OAuthClientsController : ControllerBase
{
    private readonly IOAuthClientAdministrationService _clients;
    private readonly IOptionsSnapshot<IBeamAccessControlOptions> _accessOptions;

    public OAuthClientsController(
        IOAuthClientAdministrationService clients,
        IOptionsSnapshot<IBeamAccessControlOptions> accessOptions)
    {
        _clients = clients;
        _accessOptions = accessOptions;
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid tenantId, CancellationToken ct)
    {
        if (!IsAuthorized(tenantId))
            return Forbidden();
        return Ok(await _clients.ListAsync(tenantId, ct).ConfigureAwait(false));
    }

    [HttpGet("{clientId}")]
    public async Task<IActionResult> Get(Guid tenantId, string clientId, CancellationToken ct)
    {
        if (!IsAuthorized(tenantId))
            return Forbidden();
        return await ExecuteAsync(() => _clients.GetAsync(tenantId, clientId, ct)).ConfigureAwait(false);
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid tenantId, [FromBody] CreateOAuthClientRequest request, CancellationToken ct)
    {
        if (!IsAuthorized(tenantId))
            return Forbidden();
        return await ExecuteAsync(() => _clients.CreateAsync(tenantId, request, ct)).ConfigureAwait(false);
    }

    [HttpPut("{clientId}")]
    public async Task<IActionResult> Update(
        Guid tenantId,
        string clientId,
        [FromBody] UpdateOAuthClientRequest request,
        CancellationToken ct)
    {
        if (!IsAuthorized(tenantId))
            return Forbidden();
        return await ExecuteAsync(() => _clients.UpdateAsync(tenantId, clientId, request, ct)).ConfigureAwait(false);
    }

    [HttpPost("{clientId}/rotate-secret")]
    public async Task<IActionResult> RotateSecret(Guid tenantId, string clientId, CancellationToken ct)
    {
        if (!IsAuthorized(tenantId))
            return Forbidden();
        return await ExecuteAsync(() => _clients.RotateSecretAsync(tenantId, clientId, ct)).ConfigureAwait(false);
    }

    [HttpPost("{clientId}/disable")]
    public async Task<IActionResult> Disable(Guid tenantId, string clientId, CancellationToken ct)
    {
        if (!IsAuthorized(tenantId))
            return Forbidden();
        return await ExecuteAsync(() => _clients.DisableAsync(tenantId, clientId, ct)).ConfigureAwait(false);
    }

    [HttpPost("{clientId}/revoke")]
    public async Task<IActionResult> Revoke(Guid tenantId, string clientId, CancellationToken ct)
    {
        if (!IsAuthorized(tenantId))
            return Forbidden();
        return await ExecuteAsync(() => _clients.RevokeAsync(tenantId, clientId, ct)).ConfigureAwait(false);
    }

    private bool IsAuthorized(Guid tenantId) =>
        IdentityApiAuthorization.TryAuthorizeHumanTenantOperation(
            User,
            tenantId,
            _accessOptions.Value,
            _accessOptions.Value.OAuthClientManagementPermissionNames,
            out _);

    private ObjectResult Forbidden() =>
        StatusCode(StatusCodes.Status403Forbidden, new { message = "Forbidden." });

    private async Task<IActionResult> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return Ok(await operation().ConfigureAwait(false));
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
}
