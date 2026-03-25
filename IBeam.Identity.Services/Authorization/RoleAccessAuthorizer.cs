using System.Reflection;
using System.Security.Claims;
using IBeam.Identity.Authorization;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;

namespace IBeam.Identity.Services.Authorization;

public sealed class RoleAccessAuthorizer : IRoleAccessAuthorizer
{
    private const string RoleClaimType = "role";
    private const string RoleIdClaimType = "rid";
    private const string RoleIdAltClaimType = "role_id";

    public bool IsAuthorized(ClaimsPrincipal principal, Type serviceType, string publicMethodName)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (string.IsNullOrWhiteSpace(publicMethodName))
            throw new IdentityValidationException("publicMethodName is required.");

        var methods = serviceType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(x => string.Equals(x.Name, publicMethodName.Trim(), StringComparison.Ordinal))
            .ToList();

        if (methods.Count == 0)
            throw new IdentityValidationException($"Public method '{publicMethodName}' was not found on '{serviceType.Name}'.");
        if (methods.Count > 1)
            throw new IdentityValidationException($"Method '{publicMethodName}' on '{serviceType.Name}' is overloaded. Use MethodInfo overload.");

        return IsAuthorized(principal, methods[0]);
    }

    public bool IsAuthorized(ClaimsPrincipal principal, MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);

        if (principal?.Identity?.IsAuthenticated != true)
            return false;

        var requirement = ResolveRequirement(method);
        if (!requirement.HasValues)
            return true;

        var userRoleNames = principal.Claims
            .Where(x =>
                string.Equals(x.Type, RoleClaimType, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Type, ClaimTypes.Role, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var userRoleIds = principal.Claims
            .Where(x =>
                string.Equals(x.Type, RoleIdClaimType, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Type, RoleIdAltClaimType, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .Where(x => Guid.TryParse(x, out _))
            .Select(Guid.Parse)
            .ToHashSet();

        return requirement.RoleNames.Overlaps(userRoleNames) || requirement.RoleIds.Overlaps(userRoleIds);
    }

    public void EnsureAuthorized(ClaimsPrincipal principal, Type serviceType, string publicMethodName)
    {
        if (!IsAuthorized(principal, serviceType, publicMethodName))
            throw new IdentityUnauthorizedException(
                $"Role access denied for service '{serviceType.Name}.{publicMethodName}'.");
    }

    public void EnsureAuthorized(ClaimsPrincipal principal, MethodInfo method)
    {
        if (!IsAuthorized(principal, method))
            throw new IdentityUnauthorizedException(
                $"Role access denied for service method '{method.DeclaringType?.Name}.{method.Name}'.");
    }

    private static RoleRequirement ResolveRequirement(MethodInfo method)
    {
        var methodAllowAll = method.GetCustomAttributes<AllowAllRoleAccessAttribute>(inherit: true).Any();
        var methodNames = method
            .GetCustomAttributes<RoleAccessAttribute>(inherit: true)
            .SelectMany(x => x.RoleNames)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var methodIds = method
            .GetCustomAttributes<RoleAccessIdAttribute>(inherit: true)
            .SelectMany(x => x.RoleIds)
            .Where(x => Guid.TryParse(x, out _))
            .Select(Guid.Parse)
            .ToHashSet();

        // Method-level attributes always override class-level attributes.
        if (methodAllowAll)
            return RoleRequirement.Empty;
        if (methodNames.Count > 0 || methodIds.Count > 0)
            return new RoleRequirement(methodNames, methodIds);

        var type = method.DeclaringType;
        if (type is null)
            return RoleRequirement.Empty;

        var typeAllowAll = type.GetCustomAttributes<AllowAllRoleAccessAttribute>(inherit: true).Any();
        var typeNames = type
            .GetCustomAttributes<RoleAccessAttribute>(inherit: true)
            .SelectMany(x => x.RoleNames)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var typeIds = type
            .GetCustomAttributes<RoleAccessIdAttribute>(inherit: true)
            .SelectMany(x => x.RoleIds)
            .Where(x => Guid.TryParse(x, out _))
            .Select(Guid.Parse)
            .ToHashSet();

        if (typeAllowAll)
            return RoleRequirement.Empty;
        if (typeNames.Count == 0 && typeIds.Count == 0)
            return RoleRequirement.Empty;

        return new RoleRequirement(typeNames, typeIds);
    }

    private readonly record struct RoleRequirement(HashSet<string> RoleNames, HashSet<Guid> RoleIds)
    {
        public static RoleRequirement Empty { get; } =
            new(new HashSet<string>(StringComparer.OrdinalIgnoreCase), new HashSet<Guid>());

        public bool HasValues => RoleNames.Count > 0 || RoleIds.Count > 0;
    }
}
