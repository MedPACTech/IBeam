using System.Runtime.CompilerServices;
using System.Security.Claims;

namespace IBeam.Identity.Interfaces;

public static class PermissionAccessAuthorizerExtensions
{
    public static Task EnsureAuthorizedForCurrentMethodAsync(
        this IPermissionAccessAuthorizer authorizer,
        ClaimsPrincipal principal,
        object serviceInstance,
        CancellationToken ct = default,
        [CallerMemberName] string methodName = "")
    {
        ArgumentNullException.ThrowIfNull(authorizer);
        ArgumentNullException.ThrowIfNull(serviceInstance);

        return authorizer.EnsureAuthorizedAsync(principal, serviceInstance.GetType(), methodName, ct);
    }
}
