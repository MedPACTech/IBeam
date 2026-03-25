using System.Reflection;
using System.Security.Claims;

namespace IBeam.Identity.Interfaces;

public interface IPermissionAccessAuthorizer
{
    Task<bool> IsAuthorizedAsync(
        ClaimsPrincipal principal,
        Type serviceType,
        string publicMethodName,
        CancellationToken ct = default);

    Task<bool> IsAuthorizedAsync(
        ClaimsPrincipal principal,
        MethodInfo method,
        CancellationToken ct = default);

    Task EnsureAuthorizedAsync(
        ClaimsPrincipal principal,
        Type serviceType,
        string publicMethodName,
        CancellationToken ct = default);

    Task EnsureAuthorizedAsync(
        ClaimsPrincipal principal,
        MethodInfo method,
        CancellationToken ct = default);
}
