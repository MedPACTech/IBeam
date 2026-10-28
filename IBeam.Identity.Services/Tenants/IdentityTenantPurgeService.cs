using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Identity.Services.Tenants;

public sealed class IdentityTenantPurgeService(IEnumerable<IIdentityTenantPurgeResourceStore> resources, IIdentityTenantPurgeAuthority authority,
    IServiceOperationExecutor operations) : IIdentityTenantPurgeService
{
    private readonly IIdentityTenantPurgeResourceStore[] stores = resources.ToArray();
    public bool SupportsTenantPurge => stores.Length == IdentityTenantPurgeInventory.Keys.Count && stores.All(x => x.ProtocolVersion == 1 && x.ProtectsAllWriters) &&
        stores.Select(x => x.Key).Order(StringComparer.Ordinal).SequenceEqual(IdentityTenantPurgeInventory.Keys.Order(StringComparer.Ordinal));
    public Task<bool> HasPurgeMarkerAsync(IdentityTenantPurgeScope scope, CancellationToken ct = default) => operations.ExecuteAsync(this, async token => {
        RequireSupport(); return await stores.Single(x => x.Key == "tenant-metadata").HasPurgeMarkerAsync(scope, token);
    }, new ServiceOperationExecutionOptions { OperationName = "identity.tenant-purge.marker", EntityId = scope.TenantId, TenantId = scope.TenantId, PermissionEnabled = false, AuditEnabled = false }, ct);
    public Task PreviewAsync(Guid tenantId, CancellationToken ct = default) => Run(tenantId, async token => {
        RequireSupport(); foreach (var store in stores) await store.PreviewAsync(tenantId, token);
    }, ct);
    public Task FenceAsync(IdentityTenantPurgeScope scope, CancellationToken ct = default) => Run(scope.TenantId, async token => {
        var authorization = await Authorize(scope, token);
        // Conditional native tenant disable comes first; later partial fencing never reactivates access.
        foreach (var store in Ordered()) {
            await authorization.RequireLeaseAsync(token);
            if (await store.IsFencedAsync(authorization, token)) continue;
            await store.FencePageAsync(authorization, 10, token);
            // One bounded entity-family page per lease attempt; completed checkpoints survive process restart.
            throw new IdentityTenantPurgePendingException();
        }
        foreach (var store in stores) await store.RequireFencedAsync(authorization, token);
    }, ct);
    public Task RequireFencedAsync(IdentityTenantPurgeScope scope, CancellationToken ct = default) => Run(scope.TenantId, async token => {
        var authorization = await Authorize(scope, token); foreach (var store in stores) await store.RequireFencedAsync(authorization, token);
    }, ct);
    public Task CleanupAsync(IdentityTenantPurgeScope scope, CancellationToken ct = default) => Run(scope.TenantId, async token => {
        var authorization = await Authorize(scope, token);
        foreach (var store in stores) await store.RequireFencedAsync(authorization, token);
        foreach (var store in Ordered().Reverse()) {
            await authorization.RequireLeaseAsync(token);
            if (await store.VerifyEmptyAsync(authorization, token)) continue;
            await store.CleanupPageAsync(authorization, 10, token);
            throw new IdentityTenantPurgePendingException();
        }
    }, ct);
    public Task VerifyAsync(IdentityTenantPurgeScope scope, CancellationToken ct = default) => Run(scope.TenantId, async token => {
        var authorization = await Authorize(scope, token);
        foreach (var store in stores) {
            await store.RequireFencedAsync(authorization, token);
            if (!await store.VerifyEmptyAsync(authorization, token)) throw new IdentityProviderException("Tenant Identity cleanup is incomplete.");
        }
    }, ct);
    private IEnumerable<IIdentityTenantPurgeResourceStore> Ordered() => IdentityTenantPurgeInventory.Keys.Select(key => stores.Single(x => x.Key == key));
    private void RequireSupport() { if (!SupportsTenantPurge) throw new IdentityProviderException("Tenant Identity cleanup requires compatible protected providers."); }
    private async Task<IdentityTenantPurgeAuthorization> Authorize(IdentityTenantPurgeScope s, CancellationToken ct)
    {
        RequireSupport();
        if (s.OrganizationId == Guid.Empty || s.ApplicationId == Guid.Empty || s.RealmId == Guid.Empty || s.TenantId == Guid.Empty || s.ActorUserId == Guid.Empty || s.JobId == Guid.Empty ||
            string.IsNullOrWhiteSpace(s.ApplicationKey) || string.IsNullOrWhiteSpace(s.Environment) || string.IsNullOrWhiteSpace(s.ExpectedTenantVersion) || s.ExpectedTenantVersion == "*")
            throw new IdentityUnauthorizedException("Tenant purge authority is unavailable.");
        var lease = await authority.RequireConfirmedAsync(s, ct) ?? throw new IdentityUnauthorizedException("Tenant purge authority is unavailable.");
        await lease.RequireAsync(ct); return new(s, lease);
    }
    private Task Run(Guid tenant, Func<CancellationToken, Task> action, CancellationToken ct) => operations.ExecuteAsync(this, async token => {
        try { await action(token); }
        catch (Exception ex) when (ex is not (IdentityUnauthorizedException or IdentityProviderException or IdentityConcurrencyException or IdentityTenantPurgePendingException) &&
            !(ex is OperationCanceledException && token.IsCancellationRequested)) { throw new IdentityProviderException("Tenant Identity cleanup is unavailable."); }
    }, new ServiceOperationExecutionOptions { OperationName = "identity.tenant-purge", EntityId = tenant, TenantId = tenant, PermissionEnabled = false, AuditEnabled = false }, ct);
}
public static class IdentityTenantPurgeServiceExtensions
{
    public static IServiceCollection AddIBeamIdentityTenantPurge(this IServiceCollection services)
    { services.TryAddScoped<IIdentityTenantPurgeService, IdentityTenantPurgeService>(); return services; }
}
