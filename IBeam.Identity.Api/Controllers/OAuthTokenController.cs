using System.Text;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace IBeam.Identity.Api.Controllers;

[ApiController]
[AllowAnonymous]
public sealed class OAuthTokenController(IOAuthTokenService tokens) : ControllerBase
{
    [HttpPost("/oauth/token")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Token([FromForm] OAuthTokenHttpRequest request, CancellationToken ct)
    {
        NoStore();
        var credentials = ClientCredentials(request.ClientId, request.ClientSecret);
        try
        {
            var result = await tokens.ExchangeAsync(new(
                request.GrantType, credentials.ClientId, credentials.Secret, request.Code,
                request.RedirectUri, request.CodeVerifier, request.RefreshToken, request.Resource,
                SplitScopes(request.Scope)), ct).ConfigureAwait(false);
            return Ok(new
            {
                access_token = result.AccessToken,
                token_type = result.TokenType,
                expires_in = result.ExpiresIn,
                scope = result.Scope,
                refresh_token = result.RefreshToken
            });
        }
        catch (OAuthProtocolException ex)
        {
            if (ex.Error == "invalid_client") Response.Headers.WWWAuthenticate = "Basic realm=\"oauth/token\"";
            return StatusCode(ex.Error == "invalid_client" ? 401 : 400, new { error = ex.Error, error_description = ex.Message });
        }
    }

    [HttpPost("/oauth/revoke")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Revoke([FromForm] OAuthRevocationHttpRequest request, CancellationToken ct)
    {
        NoStore();
        var credentials = ClientCredentials(request.ClientId, request.ClientSecret);
        try
        {
            await tokens.RevokeAsync(new(
                request.Token, credentials.ClientId, credentials.Secret, request.TokenTypeHint,
                request.RevokeConsent, request.Resource), ct).ConfigureAwait(false);
            return Ok();
        }
        catch (OAuthProtocolException ex)
        {
            return StatusCode(ex.Error == "invalid_client" ? 401 : 400, new { error = ex.Error, error_description = ex.Message });
        }
    }

    private (string ClientId, string? Secret) ClientCredentials(string clientId, string? secret)
    {
        var auth = Request.Headers.Authorization.ToString();
        if (!auth.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) return (clientId, secret);
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(auth[6..].Trim()));
            var separator = decoded.IndexOf(':');
            return separator > 0
                ? (Uri.UnescapeDataString(decoded[..separator]), Uri.UnescapeDataString(decoded[(separator + 1)..]))
                : (clientId, secret);
        }
        catch { return (clientId, secret); }
    }

    private void NoStore()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
    }

    private static IReadOnlyList<string> SplitScopes(string? scope) =>
        (scope ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public class OAuthTokenHttpRequest
{
    [ModelBinder(Name = "grant_type")] public string GrantType { get; set; } = string.Empty;
    [ModelBinder(Name = "client_id")] public string ClientId { get; set; } = string.Empty;
    [ModelBinder(Name = "client_secret")] public string? ClientSecret { get; set; }
    [ModelBinder(Name = "code")] public string? Code { get; set; }
    [ModelBinder(Name = "redirect_uri")] public string? RedirectUri { get; set; }
    [ModelBinder(Name = "code_verifier")] public string? CodeVerifier { get; set; }
    [ModelBinder(Name = "refresh_token")] public string? RefreshToken { get; set; }
    [ModelBinder(Name = "resource")] public string? Resource { get; set; }
    [ModelBinder(Name = "scope")] public string? Scope { get; set; }
}

public sealed class OAuthRevocationHttpRequest
{
    [ModelBinder(Name = "token")] public string Token { get; set; } = string.Empty;
    [ModelBinder(Name = "client_id")] public string ClientId { get; set; } = string.Empty;
    [ModelBinder(Name = "client_secret")] public string? ClientSecret { get; set; }
    [ModelBinder(Name = "token_type_hint")] public string? TokenTypeHint { get; set; }
    [ModelBinder(Name = "revoke_consent")] public bool RevokeConsent { get; set; }
    [ModelBinder(Name = "resource")] public string? Resource { get; set; }
}
