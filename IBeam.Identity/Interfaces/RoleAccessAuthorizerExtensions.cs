using System.Runtime.CompilerServices;
using System.Security.Claims;

namespace IBeam.Identity.Interfaces;

public static class RoleAccessAuthorizerExtensions
{
    public static void EnsureAuthorizedForCurrentMethod(
        this IRoleAccessAuthorizer authorizer,
        ClaimsPrincipal principal,
        object serviceInstance,
        [CallerMemberName] string methodName = "")
    {
        ArgumentNullException.ThrowIfNull(authorizer);
        ArgumentNullException.ThrowIfNull(serviceInstance);
        authorizer.EnsureAuthorized(principal, serviceInstance.GetType(), methodName);
    }
}
