using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace IBeam.Identity.Api.Controllers;

[ApiController]
[Authorize]
public sealed class OAuthAuthorizationController(IOAuthAuthorizationService authorization) : ControllerBase
{
    [HttpGet("/oauth/authorize")]
    public async Task<IActionResult> Prepare([FromQuery] OAuthAuthorizationHttpRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await authorization.PrepareAsync(User, request.ToModel(), ct).ConfigureAwait(false));
        }
        catch (OAuthProtocolException ex)
        {
            return ProtocolError(ex);
        }
    }

    [HttpPost("/oauth/authorize")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Decide([FromForm] OAuthAuthorizationDecisionHttpRequest request, CancellationToken ct)
    {
        try
        {
            var result = await authorization.AuthorizeAsync(User, new(request.ToModel(), request.Approved), ct).ConfigureAwait(false);
            return Redirect(BuildRedirect(result));
        }
        catch (OAuthProtocolException ex)
        {
            return ProtocolError(ex);
        }
    }

    private IActionResult ProtocolError(OAuthProtocolException ex)
    {
        if (!string.IsNullOrWhiteSpace(ex.RedirectUri))
            return Redirect(BuildRedirect(new(ex.RedirectUri, ex.State ?? string.Empty, Error: ex.Error, ErrorDescription: ex.Message)));
        return BadRequest(new { error = ex.Error, error_description = ex.Message });
    }

    private static string BuildRedirect(OAuthAuthorizationResult result)
    {
        var separator = result.RedirectUri.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var values = result.Code is not null
            ? $"code={Uri.EscapeDataString(result.Code)}&state={Uri.EscapeDataString(result.State)}"
            : $"error={Uri.EscapeDataString(result.Error ?? "server_error")}&error_description={Uri.EscapeDataString(result.ErrorDescription ?? "Authorization failed.")}&state={Uri.EscapeDataString(result.State)}";
        return $"{result.RedirectUri}{separator}{values}";
    }
}

public class OAuthAuthorizationHttpRequest
{
    [ModelBinder(Name = "response_type")]
    public string ResponseType { get; set; } = string.Empty;
    [ModelBinder(Name = "client_id")]
    public string ClientId { get; set; } = string.Empty;
    [ModelBinder(Name = "redirect_uri")]
    public string RedirectUri { get; set; } = string.Empty;
    [ModelBinder(Name = "state")]
    public string State { get; set; } = string.Empty;
    [ModelBinder(Name = "scope")]
    public string Scope { get; set; } = string.Empty;
    [ModelBinder(Name = "resource")]
    public string Resource { get; set; } = string.Empty;
    [ModelBinder(Name = "code_challenge")]
    public string CodeChallenge { get; set; } = string.Empty;
    [ModelBinder(Name = "code_challenge_method")]
    public string CodeChallengeMethod { get; set; } = string.Empty;
    [ModelBinder(Name = "tenant_id")]
    public Guid? TenantId { get; set; }

    public OAuthAuthorizationRequest ToModel() => new(
        ResponseType.Trim(), ClientId.Trim(), RedirectUri.Trim(), State.Trim(),
        Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal).ToList(),
        Resource.Trim(), CodeChallenge.Trim(), CodeChallengeMethod.Trim(), TenantId);
}

public sealed class OAuthAuthorizationDecisionHttpRequest : OAuthAuthorizationHttpRequest
{
    [ModelBinder(Name = "approved")]
    public bool Approved { get; set; }
}
