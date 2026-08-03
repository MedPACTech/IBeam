using System.Collections.Concurrent;
using System.Security.Cryptography;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Api.Controllers;

[ApiController]
[AllowAnonymous]
public sealed class OAuthMetadataController : ControllerBase
{
    private static readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> RegistrationWindows = new();
    private readonly OAuthAuthorizationServerOptions _options;
    private readonly IApiCredentialScopeCatalogProvider _scopes;
    private readonly IOAuthClientStore _clients;

    public OAuthMetadataController(
        IOptions<OAuthAuthorizationServerOptions> options,
        IApiCredentialScopeCatalogProvider scopes,
        IOAuthClientStore clients)
    {
        _options = options.Value;
        _options.Validate();
        _scopes = scopes;
        _clients = clients;
    }

    [HttpGet("/.well-known/oauth-authorization-server")]
    [HttpGet("/.well-known/openid-configuration")]
    public async Task<IActionResult> Metadata(CancellationToken ct)
    {
        if (!_options.Enabled) return NotFound();
        var issuer = _options.Issuer.TrimEnd('/');
        var catalog = await _scopes.GetScopesAsync(Guid.Empty, ct).ConfigureAwait(false);
        var scopes = catalog.Where(x => x.IsAssignable).Select(x => x.Category.ToLowerInvariant() switch
        {
            "module" => $"api-scope:{x.Key}",
            "tool" => $"tool:{x.Key}",
            "agent" => $"agent:{x.Key}",
            _ => $"{x.Category}:{x.Key}"
        }).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        return Ok(new OAuthAuthorizationServerMetadata(
            issuer, $"{issuer}/oauth/authorize", $"{issuer}/oauth/token", $"{issuer}/oauth/revoke",
            $"{issuer}/.well-known/jwks.json",
            _options.DynamicClientRegistrationEnabled ? $"{issuer}/oauth/register" : null,
            ["code"], [OAuthGrantTypes.AuthorizationCode, OAuthGrantTypes.RefreshToken, OAuthGrantTypes.ClientCredentials],
            [OAuthCodeChallengeMethods.S256], ["none", "client_secret_basic"], scopes, true));
    }

    [HttpGet("/oauth/client-metadata/{clientId}")]
    public async Task<IActionResult> ClientMetadata(string clientId, CancellationToken ct)
    {
        if (!_options.Enabled || !_options.ClientIdMetadataDocumentsEnabled) return NotFound();
        var client = await _clients.GetAsync(clientId, ct).ConfigureAwait(false);
        if (client is null || !client.IsActive) return NotFound();
        return Ok(ToMetadata(client));
    }

    [HttpPost("/oauth/register")]
    public async Task<IActionResult> Register([FromBody] DynamicOAuthClientRegistrationRequest request, CancellationToken ct)
    {
        if (!_options.Enabled || !_options.DynamicClientRegistrationEnabled) return NotFound();
        if (!WithinRegistrationLimit()) return StatusCode(429, new { error = "slow_down", error_description = "Registration rate limit exceeded." });
        if (!string.Equals(request.TokenEndpointAuthMethod, "none", StringComparison.Ordinal) ||
            request.GrantTypes.Any(x => x is not OAuthGrantTypes.AuthorizationCode and not OAuthGrantTypes.RefreshToken))
            return BadRequest(new { error = "invalid_client_metadata", error_description = "Dynamic registration supports public authorization-code clients only." });
        var now = DateTimeOffset.UtcNow;
        var client = new OAuthClientRecord(
            $"ibc_{Base64Url(RandomNumberGenerator.GetBytes(18))}", null,
            string.IsNullOrWhiteSpace(request.ClientName) ? "OAuth Client" : request.ClientName.Trim(),
            OAuthClientTypes.Public, request.RedirectUris, request.GrantTypes,
            request.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            request.Resources, true, OAuthClientStatuses.Active, null, null, now);
        try
        {
            var created = await _clients.CreateAsync(client, ct).ConfigureAwait(false);
            return StatusCode(201, ToMetadata(created));
        }
        catch (IdentityValidationException ex)
        {
            return BadRequest(new { error = "invalid_client_metadata", error_description = ex.Message });
        }
    }

    private bool WithinRegistrationLimit()
    {
        var key = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var queue = RegistrationWindows.GetOrAdd(key, _ => new());
        lock (queue)
        {
            var cutoff = DateTimeOffset.UtcNow.AddMinutes(-1);
            while (queue.Count > 0 && queue.Peek() < cutoff) queue.Dequeue();
            if (queue.Count >= _options.DynamicRegistrationRequestsPerMinute) return false;
            queue.Enqueue(DateTimeOffset.UtcNow);
            return true;
        }
    }

    private static OAuthClientMetadataDocument ToMetadata(OAuthClientRecord client) => new(
        client.ClientId, client.DisplayName, client.RedirectUris, client.AllowedGrantTypes,
        client.AllowedScopes, client.AllowedResources,
        client.ClientType == OAuthClientTypes.Public ? "none" : "client_secret_basic");

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
