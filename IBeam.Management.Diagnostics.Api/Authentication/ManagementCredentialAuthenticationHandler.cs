using System.Text.Encodings.Web;
using IBeam.Management.Services.Credentials;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IBeam.Management.Diagnostics.Api.Authentication;

public sealed class ManagementCredentialAuthenticationHandler(
    IOptionsMonitor<ManagementCredentialAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IManagementCredentialAuthenticationService authentication) : AuthenticationHandler<ManagementCredentialAuthenticationOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Path.StartsWithSegments("/api/management-diagnostics")) return AuthenticateResult.NoResult();
        if (!Request.Headers.TryGetValue(ManagementCredentialAuthenticationDefaults.HeaderName, out var values))
            return AuthenticateResult.NoResult();
        // One dedicated header only; reject ambiguous credentials and never accept query/body/Bearer key fallbacks.
        if (values.Count != 1 || Request.Headers.ContainsKey("Authorization") || string.IsNullOrEmpty(values[0]))
            return AuthenticateResult.Fail("Management authentication was not completed.");
        try
        {
            var principal = await authentication.AuthenticateAsync(values[0]!, Context.Connection.RemoteIpAddress, Context.RequestAborted);
            return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
        }
        catch (ManagementAccessDeniedException)
        {
            return AuthenticateResult.Fail("Management authentication was not completed.");
        }
    }
}
