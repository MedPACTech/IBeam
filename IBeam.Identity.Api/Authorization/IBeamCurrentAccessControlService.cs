using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using Microsoft.AspNetCore.Http;

namespace IBeam.Identity.Api.Authorization;

public sealed class IBeamCurrentAccessControlService : IIBeamCurrentAccessControlService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IIBeamAccessControlService _access;

    public IBeamCurrentAccessControlService(
        IHttpContextAccessor httpContextAccessor,
        IIBeamAccessControlService access)
    {
        _httpContextAccessor = httpContextAccessor;
        _access = access;
    }

    public Task<bool> HasPermissionAsync(string permissionKey, CancellationToken ct = default)
        => _access.HasPermissionAsync(CurrentUser(), permissionKey, ct);

    public Task<bool> HasResourceAccessAsync(
        string resourceType,
        string resourceId,
        string minimumAccessLevel = AccessLevels.View,
        CancellationToken ct = default)
        => _access.HasResourceAccessAsync(CurrentUser(), resourceType, resourceId, minimumAccessLevel, ct);

    public Task<bool> HasResourceAccessAsync(
        string resourceType,
        Guid resourceId,
        string minimumAccessLevel = AccessLevels.View,
        CancellationToken ct = default)
        => HasResourceAccessAsync(resourceType, resourceId.ToString("D"), minimumAccessLevel, ct);

    public Task RequirePermissionAsync(string permissionKey, CancellationToken ct = default)
        => _access.RequirePermissionAsync(CurrentUser(), permissionKey, ct);

    public Task RequireResourceAccessAsync(
        string resourceType,
        string resourceId,
        string minimumAccessLevel = AccessLevels.View,
        CancellationToken ct = default)
        => _access.RequireResourceAccessAsync(CurrentUser(), resourceType, resourceId, minimumAccessLevel, ct);

    public Task RequireResourceAccessAsync(
        string resourceType,
        Guid resourceId,
        string minimumAccessLevel = AccessLevels.View,
        CancellationToken ct = default)
        => RequireResourceAccessAsync(resourceType, resourceId.ToString("D"), minimumAccessLevel, ct);

    private System.Security.Claims.ClaimsPrincipal CurrentUser()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            throw new IdentityUnauthorizedException("Authenticated user is required.");

        return user;
    }
}
