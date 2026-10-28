using System.Security.Cryptography;
using System.Text.Json;
using IBeam.Identity.Events;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace IBeam.Identity.Services.Tenants;

/// <summary>Optional app projection coordination. Reads native metadata; never changes native roles or membership.</summary>
public sealed class IdentityMembershipProjectionService(IIdentityMembershipProjectionSource source, IIdentityMembershipProjectionStore store,
    IIdentityMembershipProjectionWriteFence appFence, IServiceOperationExecutor operations) : IIdentityMembershipProjectionService, IIdentityMembershipEventDelivery
{
    public int ProtocolVersion => 1;
    public Task<string> GetVersionAsync(IdentityMembershipEventScope scope, CancellationToken ct = default) => Run(async token => {
        await source.RequireScopeAsync(scope, token); await appFence.RequireMutableAsync(scope, token); var version = await store.GetVersionAsync(scope, token);
        if (!Version(version)) throw new IdentityProviderException("Projection version is unavailable."); return version;
    }, ct);
    public Task<IdentityMembershipProjectionReceipt> RepairAsync(IdentityMembershipProjectionIntent intent, IReadOnlyList<Guid> userIds, string expectedVersion, CancellationToken ct = default)
        => Run(async token => {
            if (intent is null || intent.Kind != "repair" || intent.Id == Guid.Empty || !Version(expectedVersion) || userIds is not { Count: > 0 and <= 96 } ||
                userIds.Any(x => x == Guid.Empty) || userIds.Distinct().Count() != userIds.Count) throw new IdentityValidationException("A bounded exact-version projection intent is required.");
            var bound = intent with { IntentHash = Hash(new { Intent = intent, UserIds = userIds.Order().ToArray(), expectedVersion }) };
            await source.RequireScopeAsync(bound.Scope, token); await appFence.RequireMutableAsync(bound.Scope, token);
            var prior = await store.FindAsync(bound, token); if (prior is not null) return Receipt(prior, bound);
            return await Apply(bound, userIds, expectedVersion, token);
        }, ct);
    public Task<IdentityMembershipProjectionReceipt?> FindRepairAsync(IdentityMembershipProjectionIntent intent, CancellationToken ct = default)
        => Run(async token => {
            if (intent is null || intent.Kind != "repair" || intent.Id == Guid.Empty) throw new IdentityValidationException("Projection receipt identity is invalid.");
            await source.RequireScopeAsync(intent.Scope, token); await appFence.RequireMutableAsync(intent.Scope, token); var receipt = await store.FindAsync(intent, token);
            return receipt is null ? null : Receipt(receipt, intent);
        }, ct);
    public async Task DeliverAsync(TenantMembershipChangedEvent committed, CancellationToken ct = default)
        => await Run(async token => {
            var canonical = IdentityTenantMembershipEventProtocol.ProjectCommitted(new(IdentityMembershipCommitDisposition.Committed, committed.EventId, committed.Scope,
                committed.OperationId, committed.Sequence, committed.OccurredUtc, committed.Actor, committed.Changes, committed.ProtectedOwnerRoleIds));
            if (canonical is null || committed.ContractVersion != 1 || canonical.Kind != committed.Kind) throw new IdentityProviderException("Committed projection event is invalid.");
            var intent = new IdentityMembershipProjectionIntent(canonical.EventId, "event", canonical.Scope, canonical.Actor) { IntentHash = Hash(canonical) };
            await source.RequireScopeAsync(intent.Scope, token); await appFence.RequireMutableAsync(intent.Scope, token);
            if (await store.FindAsync(intent, token) is { } prior) { _ = Receipt(prior, intent); return true; }
            var version = await store.GetVersionAsync(intent.Scope, token);
            _ = await Apply(intent, canonical.Changes.Select(x => x.UserId).ToArray(), version, token); return true;
        }, ct);
    private async Task<IdentityMembershipProjectionReceipt> Apply(IdentityMembershipProjectionIntent intent, IReadOnlyList<Guid> ids, string version, CancellationToken ct)
    {
        if (!Version(version)) throw new IdentityProviderException("Projection version is unavailable.");
        var current = await source.ReadCurrentAsync(intent.Scope, ids, ct);
        if (current.Scope != intent.Scope || current.Members.Count != ids.Count || !current.Members.Select(x => x.UserId).Order().SequenceEqual(ids.Order()) ||
            current.Sequence < 0 || current.Members.Any(x => x.Sequence < 0 || x.Sequence > current.Sequence || x.State is { Revision: var revision } && revision == Guid.Empty))
            throw new IdentityProviderException("Native projection snapshot is invalid.");
        var fence = new Fence(source, appFence, current); await fence.RequireAsync(ct);
        // The app store also joins its own durable local purge guard and compares per-subject revisions/sequences.
        return Receipt(await store.ApplyAsync(intent, current, version, fence, ct), intent);
    }
    private static IdentityMembershipProjectionReceipt Receipt(IdentityMembershipProjectionReceipt receipt, IdentityMembershipProjectionIntent intent)
    {
        if (receipt is null || receipt.Id != intent.Id || receipt.Kind != intent.Kind || receipt.Scope != intent.Scope || !Version(receipt.Version) ||
            receipt.Applied is < 0 or > 96 || receipt.Skipped is < 0 or > 96 || receipt.Applied + receipt.Skipped > 96) throw new IdentityProviderException("Projection receipt is invalid.");
        return receipt with { Scope = receipt.Scope with { } };
    }
    private Task<T> Run<T>(Func<CancellationToken, Task<T>> body, CancellationToken ct) => operations.ExecuteAsync(this, async token => {
        if (source.ProtocolVersion != 1 || store.ProtocolVersion != 1 || !store.Durable || appFence.ProtocolVersion != 1) throw new IdentityProviderException("Compatible durable projection providers and write fencing are required.");
        try { using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(30)); return await body(deadline.Token); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (IdentityConcurrencyException) { throw new IdentityConcurrencyException(); }
        catch (IdentityValidationException) { throw new IdentityValidationException("Projection request metadata is invalid."); }
        catch { throw new IdentityProviderException("Membership projection is unavailable; reconcile the original intent."); }
    }, new ServiceOperationExecutionOptions { OperationName = "identity.tenantmemberships.projection", PermissionEnabled = false, AuditEnabled = false }, ct);
    private static bool Version(string? value) => value is { Length: > 0 and <= 256 } && value != "*" && value.Trim() == value && !value.Any(char.IsControl);
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    private sealed class Fence(IIdentityMembershipProjectionSource source, IIdentityMembershipProjectionWriteFence app, IdentityMembershipProjectionSnapshot snapshot) : IIdentityMembershipProjectionCommitFence
    { public async Task RequireAsync(CancellationToken ct) { await app.RequireMutableAsync(snapshot.Scope, ct); await source.RequireCurrentAsync(snapshot, ct); await app.RequireMutableAsync(snapshot.Scope, ct); } }
}
public static class IdentityMembershipProjectionExtensions
{
    public static IServiceCollection AddIBeamIdentityMembershipProjection(this IServiceCollection services, bool enabled = false)
    {
        if (!enabled || services.Any(x => x.ServiceType == typeof(Registration))) return services;
        if (services.Any(x => x.ServiceType == typeof(IIdentityMembershipEventDelivery))) throw new InvalidOperationException("Compose existing receivers explicitly before selecting the projection receiver.");
        services.AddSingleton<Registration>(); services.TryAddScoped<IdentityMembershipProjectionService>();
        services.TryAddScoped<IIdentityMembershipProjectionService>(p => p.GetRequiredService<IdentityMembershipProjectionService>());
        services.AddScoped<IIdentityMembershipEventDelivery>(p => p.GetRequiredService<IdentityMembershipProjectionService>());
        services.AddHostedService<Validate>(); return services;
    }
    private sealed class Registration;
    private sealed class Validate(IServiceScopeFactory scopes) : IHostedService
    {
        public Task StartAsync(CancellationToken ct) { using var scope = scopes.CreateScope(); var p = scope.ServiceProvider;
            if (p.GetRequiredService<IIdentityMembershipProjectionSource>().ProtocolVersion != 1 || p.GetRequiredService<IIdentityMembershipProjectionStore>() is not { ProtocolVersion: 1, Durable: true } ||
                p.GetRequiredService<IIdentityMembershipProjectionWriteFence>().ProtocolVersion != 1) throw new InvalidOperationException("Native metadata, atomic app projection storage and an app write fence are required.");
            _ = p.GetRequiredService<IIdentityMembershipProjectionService>(); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
