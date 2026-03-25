using System.Reflection;
using System.Security.Claims;
using IBeam.Identity.Authorization;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;

namespace IBeam.Identity.Services.Authorization;

public sealed class PermissionAccessAuthorizer : IPermissionAccessAuthorizer
{
    private readonly IRoleAccessAuthorizer _roleAccessAuthorizer;
    private readonly IPermissionGrantResolver _permissionGrantResolver;

    public PermissionAccessAuthorizer(
        IRoleAccessAuthorizer roleAccessAuthorizer,
        IPermissionGrantResolver permissionGrantResolver)
    {
        _roleAccessAuthorizer = roleAccessAuthorizer ?? throw new ArgumentNullException(nameof(roleAccessAuthorizer));
        _permissionGrantResolver = permissionGrantResolver ?? throw new ArgumentNullException(nameof(permissionGrantResolver));
    }

    public async Task<bool> IsAuthorizedAsync(
        ClaimsPrincipal principal,
        Type serviceType,
        string publicMethodName,
        CancellationToken ct = default)
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

        return await IsAuthorizedAsync(principal, methods[0], ct).ConfigureAwait(false);
    }

    public async Task<bool> IsAuthorizedAsync(
        ClaimsPrincipal principal,
        MethodInfo method,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(method);

        if (principal?.Identity?.IsAuthenticated != true)
            return false;

        var permissionRequirement = ResolvePermissionRequirement(method);

        if (permissionRequirement.Mode == PermissionRequirementMode.None)
            return _roleAccessAuthorizer.IsAuthorized(principal, method);

        if (permissionRequirement.Mode == PermissionRequirementMode.AllowAll)
            return true;

        var tenantClaim = principal.FindFirst("tid")?.Value;
        if (!Guid.TryParse(tenantClaim, out var tenantId))
            return false;

        var grants = await _permissionGrantResolver
            .ResolveAsync(tenantId, permissionRequirement.PermissionNames, permissionRequirement.PermissionIds, ct)
            .ConfigureAwait(false);

        if (!grants.HasAnyGrant)
            return false;

        var userRoleNames = principal.Claims
            .Where(x =>
                string.Equals(x.Type, "role", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Type, ClaimTypes.Role, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var userRoleIds = principal.Claims
            .Where(x =>
                string.Equals(x.Type, "rid", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Type, "role_id", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .Where(x => Guid.TryParse(x, out _))
            .Select(Guid.Parse)
            .ToHashSet();

        return grants.RoleNames.Any(userRoleNames.Contains) ||
               grants.RoleIds.Any(userRoleIds.Contains);
    }

    public async Task EnsureAuthorizedAsync(
        ClaimsPrincipal principal,
        Type serviceType,
        string publicMethodName,
        CancellationToken ct = default)
    {
        if (!await IsAuthorizedAsync(principal, serviceType, publicMethodName, ct).ConfigureAwait(false))
            throw new IdentityUnauthorizedException($"Permission access denied for service '{serviceType.Name}.{publicMethodName}'.");
    }

    public async Task EnsureAuthorizedAsync(
        ClaimsPrincipal principal,
        MethodInfo method,
        CancellationToken ct = default)
    {
        if (!await IsAuthorizedAsync(principal, method, ct).ConfigureAwait(false))
            throw new IdentityUnauthorizedException($"Permission access denied for service method '{method.DeclaringType?.Name}.{method.Name}'.");
    }

    private static PermissionRequirement ResolvePermissionRequirement(MethodInfo method)
    {
        var methodAllowAll = method.GetCustomAttributes<AllowAllRoleAccessAttribute>(inherit: true).Any();
        var methodNames = method
            .GetCustomAttributes<PermissionAccessAttribute>(inherit: true)
            .SelectMany(x => x.PermissionNames)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var methodIds = method
            .GetCustomAttributes<PermissionAccessIdAttribute>(inherit: true)
            .SelectMany(x => x.PermissionIds)
            .Where(x => Guid.TryParse(x, out _))
            .Select(Guid.Parse)
            .ToHashSet();

        if (methodAllowAll)
            return PermissionRequirement.AllowAll;
        if (methodNames.Count > 0 || methodIds.Count > 0)
            return new PermissionRequirement(PermissionRequirementMode.RequireMappedGrant, methodNames.ToList(), methodIds.ToList());

        var type = method.DeclaringType;
        if (type is null)
            return PermissionRequirement.None;

        var typeAllowAll = type.GetCustomAttributes<AllowAllRoleAccessAttribute>(inherit: true).Any();
        var typeNames = type
            .GetCustomAttributes<PermissionAccessAttribute>(inherit: true)
            .SelectMany(x => x.PermissionNames)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var typeIds = type
            .GetCustomAttributes<PermissionAccessIdAttribute>(inherit: true)
            .SelectMany(x => x.PermissionIds)
            .Where(x => Guid.TryParse(x, out _))
            .Select(Guid.Parse)
            .ToHashSet();

        if (typeAllowAll)
            return PermissionRequirement.AllowAll;
        if (typeNames.Count > 0 || typeIds.Count > 0)
            return new PermissionRequirement(PermissionRequirementMode.RequireMappedGrant, typeNames.ToList(), typeIds.ToList());

        return PermissionRequirement.None;
    }

    private enum PermissionRequirementMode
    {
        None = 0,
        AllowAll = 1,
        RequireMappedGrant = 2
    }

    private sealed record PermissionRequirement(
        PermissionRequirementMode Mode,
        IReadOnlyList<string> PermissionNames,
        IReadOnlyList<Guid> PermissionIds)
    {
        public static PermissionRequirement None { get; } =
            new(PermissionRequirementMode.None, Array.Empty<string>(), Array.Empty<Guid>());

        public static PermissionRequirement AllowAll { get; } =
            new(PermissionRequirementMode.AllowAll, Array.Empty<string>(), Array.Empty<Guid>());
    }
}
