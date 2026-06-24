using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace IBeam.AccessControl.Api;

public static class AccessControlEndpointRouteBuilderExtensions
{
    public static RouteGroupBuilder MapIBeamAccessControl(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/api",
        string? authorizationPolicy = null)
    {
        var group = endpoints.MapGroup(prefix);

        if (string.IsNullOrWhiteSpace(authorizationPolicy))
            group.RequireAuthorization();
        else
            group.RequireAuthorization(authorizationPolicy);

        group.MapGet("/tenants/{tenantId:guid}/access-control/grants", async (
            Guid tenantId,
            string? resourceType,
            string? resourceId,
            string? subjectType,
            string? subjectId,
            IResourceAccessService access,
            CancellationToken ct) =>
        {
            try
            {
                var subject = !string.IsNullOrWhiteSpace(subjectType) || !string.IsNullOrWhiteSpace(subjectId)
                    ? new AccessSubject(subjectType ?? string.Empty, subjectId ?? string.Empty)
                    : null;

                var result = await access.ListGrantsAsync(tenantId, resourceType, resourceId, subject, ct)
                    .ConfigureAwait(false);
                return Results.Ok(result);
            }
            catch (AccessControlException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        group.MapPost("/tenants/{tenantId:guid}/access-control/grants", async (
            Guid tenantId,
            GrantResourceAccessRequest request,
            HttpContext httpContext,
            IResourceAccessService access,
            CancellationToken ct) =>
        {
            try
            {
                var result = await access.GrantAccessAsync(tenantId, request, ResolveUserId(httpContext.User), ct)
                    .ConfigureAwait(false);
                return Results.Ok(result);
            }
            catch (AccessControlException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        group.MapPut("/tenants/{tenantId:guid}/access-control/grants/{grantId:guid}", async (
            Guid tenantId,
            Guid grantId,
            UpdateResourceAccessGrantRequest request,
            IResourceAccessService access,
            CancellationToken ct) =>
        {
            try
            {
                var result = await access.UpdateGrantAsync(tenantId, grantId, request, ct)
                    .ConfigureAwait(false);
                return Results.Ok(result);
            }
            catch (AccessControlException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        group.MapDelete("/tenants/{tenantId:guid}/access-control/grants/{grantId:guid}", async (
            Guid tenantId,
            Guid grantId,
            IResourceAccessService access,
            CancellationToken ct) =>
        {
            try
            {
                await access.RevokeGrantAsync(tenantId, grantId, ct).ConfigureAwait(false);
                return Results.NoContent();
            }
            catch (AccessControlException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        group.MapPost("/tenants/{tenantId:guid}/access-control/check", async (
            Guid tenantId,
            CheckResourceAccessRequest request,
            IResourceAccessAuthorizer authorizer,
            CancellationToken ct) =>
        {
            try
            {
                var result = await authorizer.AuthorizeAsync(
                    tenantId,
                    request.ResourceType,
                    request.ResourceId,
                    request.Subject,
                    request.RequiredAccessLevel,
                    ct).ConfigureAwait(false);

                return Results.Ok(result);
            }
            catch (AccessControlException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        return group;
    }

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue("uid") ??
                  user.FindFirstValue(ClaimTypes.NameIdentifier) ??
                  user.FindFirstValue("sub");

        return Guid.TryParse(raw, out var parsed) && parsed != Guid.Empty ? parsed : null;
    }
}
