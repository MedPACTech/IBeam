using Microsoft.AspNetCore.Authorization;

namespace IBeam.Identity.Api.Authorization;

public sealed class AllowRoleIdsAttribute : AuthorizeAttribute
{
    public AllowRoleIdsAttribute(params string[] roleIds)
    {
        if (roleIds is null || roleIds.Length == 0)
            throw new ArgumentException("At least one roleId is required.", nameof(roleIds));

        var parsed = roleIds
            .Where(x => Guid.TryParse(x, out _))
            .Select(Guid.Parse)
            .Distinct()
            .ToArray();

        if (parsed.Length == 0)
            throw new ArgumentException("At least one valid roleId GUID is required.", nameof(roleIds));

        var encoded = string.Join(",", parsed.Select(x => x.ToString("D")));
        Policy = $"{RoleIdsAuthorizationPolicyProvider.PolicyPrefix}{encoded}";
    }
}
