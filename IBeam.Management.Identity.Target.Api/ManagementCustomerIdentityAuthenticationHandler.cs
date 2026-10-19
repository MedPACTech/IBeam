using System.Text.Encodings.Web;
using IBeam.Management.Services.Credentials;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using IBeam.Management.CustomerIdentity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Extensions;
namespace IBeam.Management.Identity.Target.Api;

internal sealed class ManagementCustomerIdentityAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, IManagementCredentialAuthenticationService authentication,
    IEnumerable<IManagementDelegatedActorValidator> actorValidators)
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
            if (Request.Method != "GET" || Request.Path != "/api/management-identity/info")
            {
                var operation = Context.GetEndpoint()?.Metadata.GetMetadata<ManagementCustomerIdentityOperationAttribute>()?.Operation;
                var validator = actorValidators.SingleOrDefault();
                if (operation is null || validator is null || !Request.Headers.TryGetValue("X-IBeam-Management-Actor", out var assertion) ||
                    assertion.Count != 1 || assertion[0] is not { Length: > 0 and <= 8192 } || Request.ContentLength > 32768)
                    return AuthenticateResult.Fail("Management authentication was not completed.");
                Request.EnableBuffering(bufferThreshold: 32768, bufferLimit: 32769);
                using var body = new MemoryStream();
                var bytes = new byte[4096];
                while (true)
                {
                    var count = await Request.Body.ReadAsync(bytes.AsMemory(0, (int)Math.Min(bytes.Length, 32769 - body.Length)), Context.RequestAborted);
                    if (count == 0) break;
                    body.Write(bytes, 0, count);
                    if (body.Length > 32768) return AuthenticateResult.Fail("Management authentication was not completed.");
                }
                Request.Body.Position = 0;
                var path = Context.Features.Get<IHttpRequestFeature>()?.RawTarget;
                if (string.IsNullOrEmpty(path)) path = Request.GetEncodedPathAndQuery();
                await validator.ValidateAsync(assertion[0]!, operation, new ManagementDelegationRequest {
                    Method = Request.Method, PathAndQuery = path, Body = body.ToArray() }, principal, Context.RequestAborted);
            }
            return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
        }
        catch (ManagementAccessDeniedException) { return AuthenticateResult.Fail("Management authentication was not completed."); }
        catch (IOException) { return AuthenticateResult.Fail("Management authentication was not completed."); }
    }
}
