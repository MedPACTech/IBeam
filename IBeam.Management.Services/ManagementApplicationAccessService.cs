using System.Security.Claims;
using IBeam.Management.Entities;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace IBeam.Management.Services;

/// <summary>Assignment entity service. State-provider ports keep Identity and application repositories outside this service.</summary>
public sealed class ManagementApplicationAccessService(IManagementApplicationAccessRepository access,
    IManagementApplicationStateProvider applications, IManagementSupportUserStateProvider users,
    IManagementPrincipalProvider principals, IServiceOperationExecutor operations) : IManagementApplicationAccessService
{
    public Task<ManagementApplicationAccessList> ListAsync(Guid applicationId, int pageSize = 50, string? continuationToken = null, CancellationToken ct = default)
        => Run("management.application-access.list", applicationId, async token =>
        {
            var actor = await ActorAsync(ManagementSupportOperations.AccessAdmin, token);
            await RequireAdministratorAsync(actor, token);
            await RequireAppAsync(actor.Org, applicationId, token);
            Guid? after = null;
            if (pageSize is < 1 or > 100) throw new ManagementApplicationInvalidException();
            if (continuationToken is not null)
            {
                if (!Guid.TryParseExact(continuationToken, "N", out var id) || id == Guid.Empty) throw new ManagementApplicationInvalidException();
                after = id;
            }
            var page = await access.ListAsync(actor.Org, applicationId, after, pageSize, token);
            if (page.Items.Count > pageSize || page.Items.Any(x => x.OrganizationId != actor.Org || x.ApplicationId != applicationId))
                throw new ManagementApplicationUnavailableException();
            return new ManagementApplicationAccessList(page.Items.Select(Dto).ToArray(), pageSize, page.Next?.ToString("N"));
        }, ct);

    public Task<ManagementApplicationAccessDto> PutAsync(Guid applicationId, Guid userId, ManagementApplicationAccessRequest request, CancellationToken ct = default)
        => Run("management.application-access.grant", applicationId, async token =>
        {
            var actor = await ActorAsync(ManagementSupportOperations.AccessAdmin, token);
            await RequireAdministratorAsync(actor, token);
            await RequireAppAsync(actor.Org, applicationId, token);
            await RequireUserAsync(actor.Org, userId, token);
            if (request is null || !ValidPermissions(request.Permissions)) throw new ManagementApplicationInvalidException();
            RequireCeiling(actor.Identity, request.Permissions);
            var current = await access.GetAsync(actor.Org, applicationId, userId, token);
            if (current is not null)
            {
                RequireBinding(current, actor.Org, applicationId, userId);
                RequireCeiling(actor.Identity, current.Permissions);
                if (!ValidVersion(request.ExpectedVersion)) throw new ManagementApplicationConflictException();
            }
            else if (request.ExpectedVersion is not null) throw new ManagementApplicationConflictException();
            var now = DateTimeOffset.UtcNow;
            var row = new ManagementApplicationAccessEntity
            {
                OrganizationId = actor.Org, ApplicationId = applicationId, UserId = userId,
                Permissions = request.Permissions.Order(StringComparer.Ordinal).ToArray(),
                CreatedUtc = current?.CreatedUtc ?? now, UpdatedUtc = now, UpdatedBy = actor.User
            };
            var saved = current is null ? await access.CreateAsync(row, token) :
                await access.ReplaceAsync(row, request.ExpectedVersion!, token);
            return Dto(saved);
        }, ct);

    public Task RevokeAsync(Guid applicationId, Guid userId, string expectedVersion, CancellationToken ct = default)
        => Run("management.application-access.revoke", applicationId, async token =>
        {
            var actor = await ActorAsync(ManagementSupportOperations.AccessAdmin, token);
            await RequireAdministratorAsync(actor, token);
            await RequireAppAsync(actor.Org, applicationId, token);
            // Revocation is allowed for disabled/deleted recipients; it cannot restore access.
            if (userId == Guid.Empty || !ValidVersion(expectedVersion)) throw new ManagementApplicationInvalidException();
            var current = await access.GetAsync(actor.Org, applicationId, userId, token) ?? throw new ManagementApplicationNotFoundException();
            RequireBinding(current, actor.Org, applicationId, userId);
            RequireCeiling(actor.Identity, current.Permissions);
            await access.ReplaceAsync(current with { RevokedUtc = DateTimeOffset.UtcNow, RevokedBy = actor.User,
                UpdatedUtc = DateTimeOffset.UtcNow, UpdatedBy = actor.User }, expectedVersion, token);
            return true;
        }, ct);

    public Task RequireAsync(Guid applicationId, string permission, CancellationToken ct = default)
        => Run("management.application-access.require", applicationId, async token =>
        {
            var actor = await ActorAsync(ManagementOperations.IdentityRead, token);
            if (!Known(permission)) throw new ManagementAccessDeniedException();
            await RequireUserAsync(actor.Org, actor.User, token);
            await RequireAppAsync(actor.Org, applicationId, token);
            var current = await access.GetAsync(actor.Org, applicationId, actor.User, token);
            if (current is null) throw new ManagementAccessDeniedException();
            RequireBinding(current, actor.Org, applicationId, actor.User);
            if (current.RevokedUtc.HasValue || !ValidPermissions(current.Permissions) ||
                !current.Permissions.Contains(permission, StringComparer.Ordinal)) throw new ManagementAccessDeniedException();
            return true;
        }, ct);

    private async Task<(ClaimsIdentity Identity, Guid Org, Guid User)> ActorAsync(string operation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var identity = ManagementPrincipalGuard.RequireIdentity(principals.GetPrincipal(), ManagementSubjectTypes.Human, operation);
        var org = ManagementPrincipalGuard.RequireGuid(identity, ManagementClaimTypes.OrganizationId);
        if (!Guid.TryParse(ManagementPrincipalGuard.RequireHumanUserId(identity), out var user) || user == Guid.Empty)
            throw new ManagementAccessDeniedException();
        return (identity, org, user);
    }
    private async Task RequireAdministratorAsync((ClaimsIdentity Identity, Guid Org, Guid User) actor, CancellationToken ct)
    {
        var user = await RequireUserAsync(actor.Org, actor.User, ct);
        if (!user.IsAdministrator) throw new ManagementAccessDeniedException();
    }
    private async Task<ManagementSupportUserState> RequireUserAsync(Guid org, Guid id, CancellationToken ct)
    {
        var user = await users.GetAsync(org, id, ct);
        if (user is null || user.OrganizationId != org || user.UserId != id || !user.IsEnabled) throw new ManagementAccessDeniedException();
        return user;
    }
    private async Task RequireAppAsync(Guid org, Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty) throw new ManagementAccessDeniedException();
        var app = await applications.GetAsync(org, id, ct);
        if (app is null || app.OrganizationId != org || app.ApplicationId != id || !app.IsEnabled) throw new ManagementAccessDeniedException();
    }
    private static bool Known(string permission) => permission is ManagementOperations.ErrorsRead or ManagementOperations.LogsRead;
    private static bool ValidPermissions(IReadOnlyList<string>? permissions)
        => permissions is { Count: > 0 and <= 2 } && permissions.All(Known) && permissions.Distinct(StringComparer.Ordinal).Count() == permissions.Count;
    private static bool ValidVersion(string? version) => !string.IsNullOrWhiteSpace(version) && version.Length <= 256 && version != "*";
    private static void RequireCeiling(ClaimsIdentity actor, IReadOnlyList<string> permissions)
    {
        if (!ValidPermissions(permissions) || permissions.Any(x => !actor.HasClaim(ManagementClaimTypes.Permission,
            x == ManagementOperations.ErrorsRead ? ManagementAccessDelegation.Errors : ManagementAccessDelegation.Logs)))
            throw new ManagementAccessDeniedException();
    }
    private static void RequireBinding(ManagementApplicationAccessEntity row, Guid org, Guid app, Guid user)
    {
        if (row.OrganizationId != org || row.ApplicationId != app || row.UserId != user) throw new ManagementAccessDeniedException();
    }
    private static ManagementApplicationAccessDto Dto(ManagementApplicationAccessEntity row) => new(row.ApplicationId, row.UserId,
        row.Permissions.ToArray(), row.RevokedUtc.HasValue, row.UpdatedUtc, row.UpdatedBy, row.RevokedUtc, row.RevokedBy, row.Version);
    private Task<T> Run<T>(string operation, Guid app, Func<CancellationToken, Task<T>> action, CancellationToken ct)
        => operations.ExecuteAsync(this, async token =>
        {
            try { return await action(token); }
            catch (Exception ex) when (ex is not (ManagementAccessDeniedException or ManagementApplicationInvalidException or
                ManagementApplicationNotFoundException or ManagementApplicationConflictException or ManagementApplicationUnavailableException or OperationCanceledException))
            { throw new ManagementApplicationUnavailableException(); }
        }, new ServiceOperationExecutionOptions { OperationName = operation, AuditAction = operation, EntityId = app,
            EntityName = nameof(ManagementApplicationAccessEntity), PermissionEnabled = false, AuditEnabled = true }, ct);
}
public static class ManagementApplicationAccessServiceExtensions
{
    public static IServiceCollection AddIBeamManagementApplicationAccess(this IServiceCollection services)
    {
        services.AddIBeamManagementApplications();
        services.TryAddScoped<IManagementApplicationAccessService, ManagementApplicationAccessService>();
        return services;
    }
}
