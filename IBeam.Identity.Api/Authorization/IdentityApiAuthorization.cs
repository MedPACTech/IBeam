using System.Security.Claims;
using System.Text.Json;
using IBeam.Identity.Options;

namespace IBeam.Identity.Api.Authorization;

internal static class IdentityApiAuthorization
{
    private const string MicrosoftTenantIdClaimType = "http://schemas.microsoft.com/identity/claims/tenantid";

    public static bool TryAuthorizeTenantOperation(
        ClaimsPrincipal user,
        Guid routeTenantId,
        IBeamAccessControlOptions options,
        IEnumerable<string>? permissionNames = null)
    {
        if (!TryResolveTenantId(user, out var tokenTenantId) || tokenTenantId != routeTenantId)
            return false;

        return HasTenantManagementRole(user, options) ||
               HasAnyPermission(user, options.TenantManagementPermissionNames) ||
               HasAnyPermission(user, permissionNames);
    }

    public static bool TryAuthorizeTenantMember(ClaimsPrincipal user, Guid routeTenantId)
        => TryResolveTenantId(user, out var tokenTenantId) && tokenTenantId == routeTenantId;

    public static bool TryAuthorizeHumanTenantOperation(
        ClaimsPrincipal user,
        Guid routeTenantId,
        IBeamAccessControlOptions options,
        IEnumerable<string>? permissionNames,
        out Guid? userId)
    {
        userId = null;

        if (IsApiCredential(user))
            return false;

        if (!TryResolveTenantId(user, out var tokenTenantId) || tokenTenantId != routeTenantId)
            return false;

        userId = ResolveUserId(user);
        return HasTenantManagementRole(user, options) ||
               HasAnyPermission(user, options.TenantManagementPermissionNames) ||
               HasAnyPermission(user, permissionNames);
    }

    public static bool TryAuthorizeHumanTenantOperation(
        ClaimsPrincipal user,
        IBeamAccessControlOptions options,
        IEnumerable<string>? permissionNames,
        out Guid tenantId,
        out Guid? userId)
    {
        tenantId = Guid.Empty;
        userId = null;

        if (IsApiCredential(user) || !TryResolveTenantId(user, out tenantId))
            return false;

        userId = ResolveUserId(user);
        return HasTenantManagementRole(user, options) ||
               HasAnyPermission(user, options.TenantManagementPermissionNames) ||
               HasAnyPermission(user, permissionNames);
    }

    public static bool TryAuthorizeAuthAttemptAdmin(ClaimsPrincipal user, IBeamAccessControlOptions options)
    {
        if (IsApiCredential(user))
            return false;

        return HasAnyPermission(user, options.AuthAttemptManagementPermissionNames) ||
               HasAnyRole(user, TenantManagementRoleNames(options).Concat(options.AuthAttemptManagementRoleNames));
    }

    public static bool TryGetCurrentUserId(ClaimsPrincipal user, out Guid userId)
    {
        userId = ResolveUserId(user) ?? Guid.Empty;
        return userId != Guid.Empty;
    }

    public static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = FindFirstClaimValue(user, "uid", ClaimTypes.NameIdentifier, "sub");
        return Guid.TryParse(raw, out var parsed) && parsed != Guid.Empty ? parsed : null;
    }

    public static bool IsApiCredential(ClaimsPrincipal user)
        => string.Equals(user.FindFirstValue("api_subject_type"), "credential", StringComparison.OrdinalIgnoreCase) ||
           !string.IsNullOrWhiteSpace(user.FindFirstValue("api_credential_id"));

    private static bool TryResolveTenantId(ClaimsPrincipal user, out Guid tenantId)
        => Guid.TryParse(FindFirstClaimValue(user, "tid", "tenant_id", MicrosoftTenantIdClaimType), out tenantId);

    private static bool HasTenantManagementRole(ClaimsPrincipal user, IBeamAccessControlOptions options)
        => HasAnyRole(user, TenantManagementRoleNames(options));

    private static IEnumerable<string> TenantManagementRoleNames(IBeamAccessControlOptions options)
        => Clean(options.OwnerRoleNames).Concat(Clean(options.AdminRoleNames));

    private static bool HasAnyRole(ClaimsPrincipal user, IEnumerable<string>? roleNames)
        => HasAnyClaimValue(user, roleNames, "role", "roles", ClaimTypes.Role);

    private static bool HasAnyPermission(ClaimsPrincipal user, IEnumerable<string>? permissionNames)
        => HasAnyClaimValue(user, permissionNames, "permission", "permissions", "scope", "scp");

    private static bool HasAnyClaimValue(ClaimsPrincipal user, IEnumerable<string>? acceptedValues, params string[] claimTypes)
    {
        var accepted = Clean(acceptedValues).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (accepted.Count == 0)
            return false;

        return FindClaimValues(user, claimTypes)
            .SelectMany(x => ExpandClaimValue(x.Value))
            .Any(accepted.Contains);
    }

    private static string? FindFirstClaimValue(ClaimsPrincipal user, params string[] claimTypes)
        => FindClaimValues(user, claimTypes)
            .Select(x => x.Value)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

    private static IEnumerable<Claim> FindClaimValues(ClaimsPrincipal user, params string[] claimTypes)
    {
        var accepted = Clean(claimTypes).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return user.Claims.Where(x => accepted.Contains(x.Type));
    }

    private static IEnumerable<string> ExpandClaimValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        var trimmed = value.Trim();
        if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
        {
            string[]? parsed = null;
            try
            {
                parsed = JsonSerializer.Deserialize<string[]>(trimmed);
            }
            catch
            {
            }

            if (parsed is not null)
            {
                foreach (var item in Clean(parsed))
                    yield return item;
                yield break;
            }
        }

        foreach (var item in trimmed.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return item;
    }

    private static IEnumerable<string> Clean(IEnumerable<string>? values)
        => values?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim()) ?? [];
}
