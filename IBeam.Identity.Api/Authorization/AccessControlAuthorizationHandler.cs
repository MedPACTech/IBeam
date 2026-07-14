using IBeam.Identity.Interfaces;
using Microsoft.AspNetCore.Authorization;

namespace IBeam.Identity.Api.Authorization;

public sealed class AccessControlAuthorizationHandler :
    AuthorizationHandler<RequirePermissionRequirement>,
    IAuthorizationHandler
{
    private readonly IIBeamAccessControlService _access;

    public AccessControlAuthorizationHandler(IIBeamAccessControlService access)
    {
        _access = access;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RequirePermissionRequirement requirement)
    {
        if (await _access.HasPermissionAsync(context.User, requirement.PermissionName).ConfigureAwait(false))
            context.Succeed(requirement);
    }

    public override async Task HandleAsync(AuthorizationHandlerContext context)
    {
        await base.HandleAsync(context).ConfigureAwait(false);

        foreach (var requirement in context.PendingRequirements.ToList())
        {
            switch (requirement)
            {
                case RequireModuleRequirement module:
                    if (await _access.HasModuleAccessAsync(context.User, module.ModuleKey, module.AccessLevel).ConfigureAwait(false))
                        context.Succeed(requirement);
                    break;
                case RequireResourceRequirement resource:
                    if (await _access.HasResourceAccessAsync(context.User, resource.ResourceType, resource.ResourceId, resource.AccessLevel).ConfigureAwait(false))
                        context.Succeed(requirement);
                    break;
            }
        }
    }
}

