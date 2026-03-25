using Microsoft.AspNetCore.Authorization;

namespace IBeam.Identity.Api.Authorization;

public sealed class AllowRolesAttribute : AuthorizeAttribute
{
    public AllowRolesAttribute(params string[] roleNames)
    {
        if (roleNames is null || roleNames.Length == 0)
            throw new ArgumentException("At least one role name is required.", nameof(roleNames));

        var normalized = roleNames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length == 0)
            throw new ArgumentException("At least one non-empty role name is required.", nameof(roleNames));

        Roles = string.Join(",", normalized);
    }
}
