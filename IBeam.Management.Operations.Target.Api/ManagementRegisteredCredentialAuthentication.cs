using System.Text.Encodings.Web;
using IBeam.Management.Services.Credentials;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IBeam.Management.Operations.Target.Api;

public static class ManagementRegisteredAuthentication
{
    public const string Scheme = "IBeam.Management.RegisteredCredential";
    public const string Policy = "IBeam.Management.RegisteredTarget";
}
internal sealed class ManagementRegisteredCredentialAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, IManagementCredentialAuthenticationService authentication)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Path.StartsWithSegments("/api/management-operations") && !Request.Path.StartsWithSegments("/api/management-tenant-data")) return AuthenticateResult.NoResult();
        if (!Request.Headers.TryGetValue("X-IBeam-Management-Key", out var values)) return AuthenticateResult.NoResult();
        if (values.Count != 1 || string.IsNullOrEmpty(values[0]) || Request.Headers.ContainsKey("Authorization")) return AuthenticateResult.Fail("Management authentication was not completed.");
        try
        {
            var principal = await authentication.AuthenticateAsync(values[0]!, Context.Connection.RemoteIpAddress, Context.RequestAborted);
            return AuthenticateResult.Success(new(principal, Scheme.Name));
        }
        catch (ManagementAccessDeniedException) { return AuthenticateResult.Fail("Management authentication was not completed."); }
    }
}
