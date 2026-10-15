using System.Security.Claims;
using IBeam.Management;
using Microsoft.AspNetCore.Http;

namespace IBeam.Management.Diagnostics.Api;

internal sealed class HttpManagementPrincipalProvider(IHttpContextAccessor context) : IManagementPrincipalProvider
{
    public ClaimsPrincipal GetPrincipal()
        => context.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());
}
