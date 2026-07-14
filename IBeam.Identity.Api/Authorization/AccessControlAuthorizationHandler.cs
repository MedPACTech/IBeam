using IBeam.Identity.Interfaces;
using IBeam.Identity.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace IBeam.Identity.Api.Authorization;

public sealed class AccessControlAuthorizationHandler :
    AuthorizationHandler<RequirePermissionRequirement>,
    IAuthorizationHandler
{
    private readonly IIBeamAccessControlService _access;
    private readonly IApiCredentialAccessService _apiCredentialAccess;

    public AccessControlAuthorizationHandler(
        IIBeamAccessControlService access,
        IApiCredentialAccessService apiCredentialAccess)
    {
        _access = access;
        _apiCredentialAccess = apiCredentialAccess;
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
                case RequireRouteResourceRequirement routeResource:
                    var resourceId = ResolveRouteResourceId(context.Resource, routeResource);
                    if (!string.IsNullOrWhiteSpace(resourceId) &&
                        await _access.HasResourceAccessAsync(context.User, routeResource.ResourceType, resourceId, routeResource.AccessLevel).ConfigureAwait(false))
                        context.Succeed(requirement);
                    break;
                case RequireApiScopeRequirement apiScope:
                    if (await TryEvaluateCredentialAsync(() => _apiCredentialAccess.HasApiScopeAsync(context.User, apiScope.ModuleKey)).ConfigureAwait(false))
                        context.Succeed(requirement);
                    break;
                case RequireToolRequirement tool:
                    if (await TryEvaluateCredentialAsync(() => _apiCredentialAccess.HasToolAccessAsync(context.User, tool.ToolKey)).ConfigureAwait(false))
                        context.Succeed(requirement);
                    break;
                case RequireAgentRequirement agent:
                    if (await TryEvaluateCredentialAsync(() => _apiCredentialAccess.CanActAsAgentAsync(context.User, agent.AgentKey)).ConfigureAwait(false))
                        context.Succeed(requirement);
                    break;
            }
        }
    }

    private static async Task<bool> TryEvaluateCredentialAsync(Func<Task<bool>> evaluate)
    {
        try
        {
            return await evaluate().ConfigureAwait(false);
        }
        catch (IdentityException)
        {
            return false;
        }
    }

    private static string? ResolveRouteResourceId(object? resource, RequireRouteResourceRequirement requirement)
    {
        if (resource is not HttpContext httpContext)
            return null;

        var candidates = new[]
        {
            requirement.RouteParameter,
            $"{requirement.ResourceType}Id",
            "resourceId",
            "id"
        };

        foreach (var candidate in candidates.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (httpContext.Request.RouteValues.TryGetValue(candidate!, out var value) &&
                !string.IsNullOrWhiteSpace(value?.ToString()))
                return value.ToString();
        }

        return null;
    }
}
