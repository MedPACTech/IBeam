using Microsoft.AspNetCore.Authorization;

namespace IBeam.Identity.Api.Authorization;

public sealed class RequireRoleIdsAuthorizationHandler : AuthorizationHandler<RequireRoleIdsRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RequireRoleIdsRequirement requirement)
    {
        if (context.User?.Identity?.IsAuthenticated != true)
            return Task.CompletedTask;

        if (requirement.RoleIds is null || requirement.RoleIds.Count == 0)
            return Task.CompletedTask;

        var userRoleIds = context.User.Claims
            .Where(x =>
                string.Equals(x.Type, "rid", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Type, "role_id", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .Where(x => Guid.TryParse(x, out _))
            .Select(Guid.Parse)
            .ToHashSet();

        if (requirement.RoleIds.Any(userRoleIds.Contains))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
