using System.Security.Claims;

namespace IBeam.Identity.Authorization;

public static class ClaimsPrincipalRoleExtensions
{
    private const string RoleClaimType = "role";
    private const string RoleIdClaimType = "rid";
    private const string RoleIdAltClaimType = "role_id";

    public static bool HasRole(this ClaimsPrincipal? principal, string roleName)
    {
        if (principal?.Identity?.IsAuthenticated != true)
            return false;
        if (string.IsNullOrWhiteSpace(roleName))
            return false;

        return principal.Claims.Any(x =>
            (string.Equals(x.Type, RoleClaimType, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(x.Type, ClaimTypes.Role, StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(x.Value, roleName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static bool HasAnyRole(this ClaimsPrincipal? principal, params string[] roleNames)
    {
        if (roleNames is null || roleNames.Length == 0)
            return false;

        var normalized = roleNames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (normalized.Count == 0)
            return false;

        return principal?.Claims.Any(x =>
            (string.Equals(x.Type, RoleClaimType, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(x.Type, ClaimTypes.Role, StringComparison.OrdinalIgnoreCase)) &&
            normalized.Contains(x.Value)) == true;
    }

    public static bool HasRoleId(this ClaimsPrincipal? principal, Guid roleId)
    {
        if (principal?.Identity?.IsAuthenticated != true)
            return false;
        if (roleId == Guid.Empty)
            return false;

        return principal.Claims
            .Where(x =>
                string.Equals(x.Type, RoleIdClaimType, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Type, RoleIdAltClaimType, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .Any(x => Guid.TryParse(x, out var parsed) && parsed == roleId);
    }

    public static bool HasAnyRoleId(this ClaimsPrincipal? principal, params Guid[] roleIds)
    {
        if (principal?.Identity?.IsAuthenticated != true)
            return false;
        if (roleIds is null || roleIds.Length == 0)
            return false;

        var expected = roleIds.Where(x => x != Guid.Empty).ToHashSet();
        if (expected.Count == 0)
            return false;

        var userIds = principal.Claims
            .Where(x =>
                string.Equals(x.Type, RoleIdClaimType, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Type, RoleIdAltClaimType, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .Where(x => Guid.TryParse(x, out _))
            .Select(Guid.Parse)
            .ToHashSet();

        return userIds.Overlaps(expected);
    }
}
