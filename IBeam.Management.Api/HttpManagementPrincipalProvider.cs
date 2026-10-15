using System.Security.Claims;
using IBeam.Management;
using Microsoft.AspNetCore.Http;

namespace IBeam.Management.Api;

internal sealed class HttpManagementPrincipalProvider(IHttpContextAccessor context) : IManagementPrincipalProvider
{
    public ClaimsPrincipal GetPrincipal()
        => context.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());
}
