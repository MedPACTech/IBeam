using Microsoft.AspNetCore.Authentication;

namespace IBeam.Management.Diagnostics.Api.Authentication;

public static class ManagementCredentialAuthenticationDefaults
{
    public const string Scheme = "IBeam.Management.Credential";
    public const string HeaderName = "X-IBeam-Management-Key";
}

public sealed class ManagementCredentialAuthenticationOptions : AuthenticationSchemeOptions;
