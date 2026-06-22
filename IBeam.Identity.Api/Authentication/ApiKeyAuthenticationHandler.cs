using System.Text.Encodings.Web;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace IBeam.Identity.Api.Authentication;

public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private readonly IApiCredentialAuthenticator _authenticator;
    private readonly IOptionsMonitor<ApiCredentialOptions> _apiCredentialOptions;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiCredentialAuthenticator authenticator,
        IOptionsMonitor<ApiCredentialOptions> apiCredentialOptions)
        : base(options, logger, encoder)
    {
        _authenticator = authenticator;
        _apiCredentialOptions = apiCredentialOptions;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var apiKey = ReadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            return AuthenticateResult.NoResult();

        var ipAddress = Context.Connection.RemoteIpAddress?.ToString();
        var result = await _authenticator.AuthenticateAsync(apiKey, ipAddress, Context.RequestAborted).ConfigureAwait(false);
        if (!result.Succeeded || result.Principal is null)
            return AuthenticateResult.Fail(result.FailureReason ?? "Invalid API key.");

        var ticket = new AuthenticationTicket(result.Principal, ApiKeyAuthenticationDefaults.AuthenticationScheme);
        return AuthenticateResult.Success(ticket);
    }

    private string? ReadApiKey()
    {
        var headerName = _apiCredentialOptions.CurrentValue.ApiKeyHeaderName;
        if (Request.Headers.TryGetValue(headerName, out var headerValues))
        {
            var value = headerValues.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        var authorization = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
            return null;

        if (authorization.StartsWith("ApiKey ", StringComparison.OrdinalIgnoreCase))
            return authorization["ApiKey ".Length..].Trim();
        if (authorization.StartsWith("Key ", StringComparison.OrdinalIgnoreCase))
            return authorization["Key ".Length..].Trim();

        return null;
    }
}
