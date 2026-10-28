using IBeam.Identity.Events;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Services.Tenants;

public sealed class IdentityMembershipEventDispatchOptions
{
    public bool Enabled { get; set; }
    public bool EnableBackgroundWorker { get; set; }
    public int LeaseSeconds { get; set; } = 45;
    public int DeliveryTimeoutSeconds { get; set; } = 20;
    public int MaximumAttempts { get; set; } = 5;
    public int RetryDelaySeconds { get; set; } = 5;
    public int ScanIntervalSeconds { get; set; } = 10;
    public bool IsValid() => LeaseSeconds is >= 10 and <= 120 && DeliveryTimeoutSeconds is >= 1 and <= 30 && LeaseSeconds >= DeliveryTimeoutSeconds + 5 &&
        MaximumAttempts is >= 1 and <= 20 && RetryDelaySeconds is >= 1 and <= 60 && ScanIntervalSeconds is >= 1 and <= 300;
}
public sealed class IdentityMembershipEventDispatcher(IIdentityMembershipEventOutboxStore store, IIdentityMembershipEventDelivery delivery,
    IOptions<IdentityMembershipEventDispatchOptions> options, TimeProvider clock, IServiceOperationExecutor operations) : IIdentityMembershipEventDispatcher
{
    public Task RunAsync(IdentityMembershipEventScope scope, int maximumEvents = 10, CancellationToken ct = default)
        => Run(async token => {
            if (maximumEvents is < 1 or > 100) throw new IdentityValidationException("Dispatch bounds are invalid.");
            for (var i = 0; i < maximumEvents; i++) {
                token.ThrowIfCancellationRequested(); await store.RequireMutableTenantAsync(scope, token);
                var current = await store.ReadFirstUndeliveredAsync(scope, token); var now = clock.GetUtcNow();
                if (current is null || current.State == "dead-letter" || current.NextAttemptUtc > now || current.State == "leased" && current.LeaseExpiresUtc > now) return;
                if (current.Attempts >= options.Value.MaximumAttempts) {
                    await store.TryReplaceAsync(current with { State = "dead-letter", LeaseId = null, LeaseExpiresUtc = null, FailureCode = current.FailureCode ?? "unknown" }, current.Version, token); return;
                }
                var leased = current with { State = "leased", Attempts = current.Attempts + 1, LeaseId = Guid.NewGuid(), LeaseExpiresUtc = now.AddSeconds(options.Value.LeaseSeconds) };
                if (!await store.TryReplaceAsync(leased, current.Version, token)) return;
                var owned = await store.ReadFirstUndeliveredAsync(scope, token);
                if (owned is null || owned.Event.EventId != leased.Event.EventId || owned.State != "leased" || owned.LeaseId != leased.LeaseId) return;
                try {
                    await store.RequireMutableTenantAsync(scope, token);
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(options.Value.DeliveryTimeoutSeconds));
                    await delivery.DeliverAsync(owned.Event, deadline.Token).WaitAsync(deadline.Token);
                } catch (Exception error) {
                    // Receiver may already have committed: redelivery uses exactly this event, never another membership mutation.
                    using var release = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try { await store.TryReplaceAsync(owned with { State = owned.Attempts >= options.Value.MaximumAttempts ? "dead-letter" : "pending",
                        NextAttemptUtc = clock.GetUtcNow().AddSeconds(Math.Min(60, options.Value.RetryDelaySeconds * owned.Attempts)), LeaseId = null, LeaseExpiresUtc = null,
                        FailureCode = error is OperationCanceledException ? token.IsCancellationRequested ? "cancelled" : "timeout" : "consumer-unavailable" }, owned.Version, release.Token); } catch { }
                    if (token.IsCancellationRequested) throw new OperationCanceledException(token); return;
                }
                // A lost acknowledgement reply remains the same durable event. Do not guess that delivery failed or mutate its payload.
                if (owned.LeaseExpiresUtc <= clock.GetUtcNow()) return;
                await store.RequireMutableTenantAsync(scope, token);
                if (!await store.TryReplaceAsync(owned with { State = "acked", LeaseId = null, LeaseExpiresUtc = null, FailureCode = null }, owned.Version, token)) return;
            }
        }, ct);
    public Task RetryDeadLetterAsync(IdentityMembershipEventScope scope, Guid eventId, string expectedVersion, CancellationToken ct = default)
        => Run(async token => {
            await store.RequireMutableTenantAsync(scope, token); var row = await store.ReadFirstUndeliveredAsync(scope, token);
            if (row is null || row.State != "dead-letter" || row.Event.EventId != eventId || row.Version != expectedVersion) throw new IdentityConcurrencyException();
            if (!await store.TryReplaceAsync(row with { State = "pending", Attempts = 0, NextAttemptUtc = clock.GetUtcNow(), FailureCode = null }, expectedVersion, token)) throw new IdentityConcurrencyException();
        }, ct);
    private Task Run(Func<CancellationToken, Task> body, CancellationToken ct) => operations.ExecuteAsync(this, async token => {
        if (!options.Value.Enabled || !options.Value.IsValid() || store.ProtocolVersion != 1 || !store.Durable || delivery.ProtocolVersion != 1)
            throw new IdentityProviderException("Explicit compatible durable membership dispatch is required.");
        try { await body(token); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IdentityValidationException or IdentityConcurrencyException) { throw; }
        catch { throw new IdentityProviderException("Membership event dispatch is unavailable; inspect the original durable event."); }
    }, new ServiceOperationExecutionOptions { OperationName = "identity.tenantmemberships.events.dispatch", PermissionEnabled = false, AuditEnabled = false }, ct);
}
public static class IdentityMembershipEventDispatchExtensions
{
    public static IServiceCollection AddIBeamIdentityMembershipEventDispatch(this IServiceCollection services, Action<IdentityMembershipEventDispatchOptions> configure)
    {
        var settings = new IdentityMembershipEventDispatchOptions(); configure(settings); if (!settings.Enabled) return services;
        if (!settings.IsValid()) throw new InvalidOperationException("Membership dispatch options are invalid.");
        if (services.Any(x => x.ServiceType == typeof(Registration))) return services;
        services.AddSingleton<Registration>(); services.AddIBeamIdentityMembershipEventContracts(); services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<IdentityMembershipEventDispatchOptions>().Configure(o => { o.Enabled = true; o.EnableBackgroundWorker = settings.EnableBackgroundWorker;
            o.LeaseSeconds = settings.LeaseSeconds; o.DeliveryTimeoutSeconds = settings.DeliveryTimeoutSeconds; o.MaximumAttempts = settings.MaximumAttempts;
            o.RetryDelaySeconds = settings.RetryDelaySeconds; o.ScanIntervalSeconds = settings.ScanIntervalSeconds; });
        services.TryAddScoped<IIdentityMembershipEventDispatcher, IdentityMembershipEventDispatcher>();
        services.AddHostedService<Validate>(); services.AddHostedService<IdentityMembershipEventBackgroundWorker>(); return services;
    }
    private sealed class Registration;
    private sealed class Validate(IServiceScopeFactory scopes) : IHostedService
    {
        public Task StartAsync(CancellationToken ct) { using var scope = scopes.CreateScope();
            if (scope.ServiceProvider.GetRequiredService<IIdentityMembershipEventOutboxStore>() is not { ProtocolVersion: 1, Durable: true } ||
                scope.ServiceProvider.GetRequiredService<IIdentityMembershipEventDelivery>().ProtocolVersion != 1)
                throw new InvalidOperationException("A native atomic event provider and an explicit durable receiver are required.");
            _ = scope.ServiceProvider.GetRequiredService<IIdentityMembershipEventDispatcher>(); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
public sealed class IdentityMembershipEventBackgroundWorker(IServiceScopeFactory scopes, IOptions<IdentityMembershipEventDispatchOptions> options) : BackgroundService
{
    private string? cursor; private long failures;
    public long FailureCount => Interlocked.Read(ref failures);
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!options.Value.EnableBackgroundWorker) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.ScanIntervalSeconds));
        while (!ct.IsCancellationRequested) { try { await ScanAsync(ct); } catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; } catch { Interlocked.Increment(ref failures); }
            if (!await timer.WaitForNextTickAsync(ct)) return; }
    }
    public async Task ScanAsync(CancellationToken ct = default)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(55));
        using var scope = scopes.CreateScope(); var store = scope.ServiceProvider.GetRequiredService<IIdentityMembershipEventOutboxStore>();
        var page = await store.ListPendingScopesAsync(10, cursor, budget.Token); cursor = page.Continuation;
        foreach (var tenant in page.Scopes.Take(10)) { try { await scope.ServiceProvider.GetRequiredService<IIdentityMembershipEventDispatcher>().RunAsync(tenant, 1, budget.Token); }
            catch (OperationCanceledException) when (budget.IsCancellationRequested) { throw; } catch { Interlocked.Increment(ref failures); } }
    }
}
