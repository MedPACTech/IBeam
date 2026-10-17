using System.Text.Json;
using IBeam.Management.Models;
using IBeam.Management.Services.Diagnostics;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Services;

public sealed class ManagementApplicationSelector(IManagementPrincipalProvider principals, IManagementSupportUserStateProvider users,
    IManagementApplicationCatalogProvider catalog, IManagementApplicationStateProvider applications,
    IManagementApplicationConnectionStateProvider connections, IManagementApplicationAccessService access,
    IOptions<PortalDiagnosticsOptions> options, TimeProvider clock, IServiceOperationExecutor operations) : IManagementApplicationSelector
{
    private sealed record Cursor(Guid After, DateTimeOffset ExpiresUtc);
    public Task<ManagementApplicationSelectionPage> ListAsync(int pageSize = 50, string? continuationToken = null, CancellationToken ct = default)
        => operations.ExecuteAsync(this, async token =>
        {
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
                var identity = ManagementPrincipalGuard.RequireIdentity(principals.GetPrincipal(), ManagementSubjectTypes.Human, ManagementOperations.IdentityRead);
                var org = ManagementPrincipalGuard.RequireGuid(identity, ManagementClaimTypes.OrganizationId);
                if (!Guid.TryParse(ManagementPrincipalGuard.RequireHumanUserId(identity), out var user) || user == Guid.Empty) throw new ManagementAccessDeniedException();
                async Task RequireUser()
                {
                    var row = await users.GetAsync(org, user, deadline.Token);
                    if (row is null || row.OrganizationId != org || row.UserId != user || !row.IsEnabled) throw new ManagementAccessDeniedException();
                }
                await RequireUser();
                if (pageSize is < 1 or > 100) throw new ManagementApplicationInvalidException();
                var codec = new DiagnosticTokenCodec(Convert.FromBase64String(options.Value.ProtectionKey));
                var context = JsonSerializer.Serialize(new { org, user, pageSize });
                var saved = continuationToken is null ? null : codec.Unprotect<Cursor>("portal-selector", context, continuationToken);
                if (saved is not null && (saved.ExpiresUtc <= clock.GetUtcNow() || saved.After == Guid.Empty)) throw new DiagnosticTokenException();
                var expires = saved?.ExpiresUtc ?? clock.GetUtcNow().AddMinutes(options.Value.TokenLifetimeMinutes);
                Guid? after = saved?.After; Guid? next = null;
                var results = new List<ManagementApplicationSelection>();
                async Task<IReadOnlyList<string>> Permissions(IBeam.Management.Entities.ManagementApplicationEntity app)
                {
                    var allowed = await connections.CapabilitiesAsync(app, deadline.Token);
                    var granted = new List<string>();
                    foreach (var permission in allowed)
                    {
                        try { await access.RequireAsync(app.ApplicationId, permission, deadline.Token); granted.Add(permission); }
                        catch (ManagementAccessDeniedException) { }
                    }
                    return granted;
                }
                // At most five bounded catalog requests; empty pages may carry a continuation.
                for (var request = 0; request < 5 && results.Count < pageSize; request++)
                {
                    var page = await catalog.ListAsync(org, after, pageSize - results.Count, deadline.Token);
                    foreach (var app in page.Items)
                    {
                        var grants = await Permissions(app);
                        if (grants.Count > 0) results.Add(new(app.ApplicationId, app.ApplicationKey, app.Name, app.Environment, grants));
                    }
                    next = page.Next; after = next;
                    if (!next.HasValue) break;
                }
                // Re-read live app and grant state before returning the selector; no authorization cache.
                var live = new List<ManagementApplicationSelection>();
                foreach (var item in results)
                {
                    var app = await applications.GetAsync(org, item.ApplicationId, deadline.Token);
                    if (app is null) continue;
                    var grants = await Permissions(app);
                    if (grants.Count > 0) live.Add(new(app.ApplicationId, app.ApplicationKey, app.Name, app.Environment, grants));
                }
                await RequireUser();
                return new ManagementApplicationSelectionPage(live, pageSize, next.HasValue ? codec.Protect("portal-selector", context, new Cursor(next.Value, expires)) : null);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ManagementConnectionTimeoutException(); }
            catch (Exception ex) when (ex is not (ManagementAccessDeniedException or ManagementApplicationInvalidException or DiagnosticTokenException or ManagementConnectionTimeoutException or OperationCanceledException))
            { throw new ManagementApplicationUnavailableException(); }
        }, new ServiceOperationExecutionOptions { OperationName = "management.portal.applications.list", AuditAction = "management.portal.applications.list",
            EntityName = "ManagementApplicationSelector", PermissionEnabled = false, AuditEnabled = true }, ct);
}
public static class ManagementApplicationSelectorExtensions
{
    public static IServiceCollection AddIBeamManagementApplicationSelector(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIBeamManagementPortalDiagnostics(configuration);
        services.TryAddScoped<IManagementApplicationCatalogProvider, ManagementApplicationStateProvider>();
        services.TryAddScoped<IManagementApplicationConnectionStateProvider, ManagementApplicationConnectionStateProvider>();
        services.TryAddScoped<IManagementApplicationSelector, ManagementApplicationSelector>();
        return services;
    }
}
