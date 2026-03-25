using System.Reflection;
using System.Security.Claims;

namespace IBeam.Identity.Interfaces;

public interface IRoleAccessAuthorizer
{
    bool IsAuthorized(ClaimsPrincipal principal, Type serviceType, string publicMethodName);
    bool IsAuthorized(ClaimsPrincipal principal, MethodInfo method);
    void EnsureAuthorized(ClaimsPrincipal principal, Type serviceType, string publicMethodName);
    void EnsureAuthorized(ClaimsPrincipal principal, MethodInfo method);
}
