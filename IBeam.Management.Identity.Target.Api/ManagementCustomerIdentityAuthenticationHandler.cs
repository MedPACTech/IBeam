using System.Text.Encodings.Web;
using IBeam.Management.Services.Credentials;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Identity.Target.Api;

internal sealed class ManagementCustomerIdentityAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, IManagementCredentialAuthenticationService authentication)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Path.StartsWithSegments("/api/management-identity")) return AuthenticateResult.NoResult();
        if (!Request.Headers.TryGetValue("X-IBeam-Management-Key", out var values)) return AuthenticateResult.NoResult();
        if (values.Count != 1 || Request.Headers.ContainsKey("Authorization") || string.IsNullOrWhiteSpace(values[0]))
            return AuthenticateResult.Fail("Management authentication was not completed.");
        try
        {
            var principal = await authentication.AuthenticateAsync(values[0]!, Context.Connection.RemoteIpAddress, Context.RequestAborted);
            return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
        }
        catch (ManagementAccessDeniedException) { return AuthenticateResult.Fail("Management authentication was not completed."); }
    }
}
