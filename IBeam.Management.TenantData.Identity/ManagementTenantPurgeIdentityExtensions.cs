using System.Security.Cryptography;
using System.Text;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Services.Tenants;
using IBeam.Management.Operations;
using IBeam.Management.TenantData;
using IBeam.Management.TenantData.Target.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace IBeam.Management.TenantData.Identity;

public sealed class ManagementTenantPurgeIdentityOptions { public bool Enabled { get; set; } }
public static class ManagementTenantPurgeIdentityExtensions
{
    public static IServiceCollection AddIBeamManagementTenantPurgeIdentity<TAppHandler>(this IServiceCollection services, Action<ManagementTenantPurgeIdentityOptions> configure)
        where TAppHandler : class, IManagementTenantPurgeHandler
    {
        var options = new ManagementTenantPurgeIdentityOptions(); configure(options); if (!options.Enabled) return services;
        if (services.Any(x => x.ServiceType == typeof(Registration) || x.ServiceType == typeof(IManagementTenantPurgeHandler) || x.ServiceType == typeof(IIdentityTenantPurgeAuthority)))
            throw new InvalidOperationException("Register the consumer handler as its concrete type before the optional Identity composition; do not install a second handler or authority.");
        services.AddSingleton<Registration>(); services.TryAddScoped<TAppHandler>(); services.AddScoped<ManagementIdentityPurgeContext>();
        services.AddScoped<IIdentityTenantPurgeAuthority, ManagementIdentityPurgeAuthority>(); services.AddIBeamIdentityTenantPurge();
        services.AddScoped<IManagementTenantPurgeHandler, IdentityPurgeHandler<TAppHandler>>();
        services.AddScoped<IManagementTenantPurgeAccessFence, IdentityPurgeFence>(); services.AddHostedService<Validate<TAppHandler>>();
        return services;
    }
    private sealed class Registration;
    private sealed class Validate<T>(IServiceScopeFactory scopes) : IHostedService where T : class, IManagementTenantPurgeHandler
    {
        public Task StartAsync(CancellationToken ct) {
            using var scope = scopes.CreateScope(); var p = scope.ServiceProvider;
            if (!p.GetRequiredService<IIdentityTenantPurgeService>().SupportsTenantPurge || p.GetRequiredService<IManagementTenantPurgeJournalRepository>().ProtocolVersion != 1 ||
                p.GetRequiredService<IManagementTenantPurgeJobService>() is not ManagementTenantPurgeJobService || p.GetRequiredService<IManagementTenantPurgeWorker>() is not ManagementTenantPurgeWorker)
                throw new InvalidOperationException("The Identity purge bridge requires protected native providers and the standard durable Management coordinator.");
            if (!p.GetServices<IManagementTenantPurgeAccessFence>().Any(x => x.ProtocolVersion == 1 && x.Key != "ibeam-identity"))
                throw new InvalidOperationException("The Identity bridge also requires the consumer's app access/write fence.");
            _ = ManagementTenantPurgeProtocol.Definition(p.GetRequiredService<IManagementTenantPurgeHandler>().Describe());
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
internal sealed class ManagementIdentityPurgeContext
{
    private readonly AsyncLocal<ManagementTenantPurgeExecution?> current = new();
    public ManagementTenantPurgeExecution Current => current.Value ?? throw new IdentityUnauthorizedException("Confirmed tenant purge authority is required.");
    public async Task Invoke(ManagementTenantPurgeExecution execution, Func<Task> action) {
        var prior = current.Value; current.Value = execution;
        try { await action(); } finally { current.Value = prior; }
    }
    public static IdentityTenantPurgeScope Scope(ManagementTenantPurgeExecution e) => new(e.ApplicationKey, e.Environment, e.Authority.OrganizationId, e.Authority.ApplicationId,
        e.Authority.RealmId, e.Authority.TenantId, e.Authority.ActorUserId, e.JobId, e.Authority.ResourceVersion ?? throw new IdentityUnauthorizedException("A confirmed native tenant version is required."));
}
internal sealed class ManagementIdentityPurgeAuthority(ManagementIdentityPurgeContext context, IManagementTenantPurgeJournalRepository journal, TimeProvider clock) : IIdentityTenantPurgeAuthority
{
    public async Task<IIdentityTenantPurgeLease> RequireConfirmedAsync(IdentityTenantPurgeScope scope, CancellationToken ct = default) {
        var execution = context.Current;
        if (ManagementIdentityPurgeContext.Scope(execution) != scope || execution.LeaseFence?.JournalProtocolVersion != 1)
            throw new IdentityUnauthorizedException("Confirmed tenant purge authority is required.");
        var lease = new Lease(scope, execution, journal, clock); await lease.ValidateConfirmedAsync(ct); return lease;
    }
    private sealed class Lease(IdentityTenantPurgeScope scope, ManagementTenantPurgeExecution execution, IManagementTenantPurgeJournalRepository journal, TimeProvider clock) : IIdentityTenantPurgeLease
    {
        public Task RequireAsync(CancellationToken ct = default) => execution.LeaseFence!.RequireJournalAsync(ct);
        public async Task ValidateConfirmedAsync(CancellationToken ct = default) {
            await execution.LeaseFence!.RequireJournalAsync(ct);
            var job = await journal.GetJobAsync(execution, ct);
            if (job is null || job.Kind != "job" || !job.WorkStarted || job.State == "cancelled" || job.Phase is not ("fencing" or "purging" or "verifying") ||
                job.LeaseId is null || job.LeaseExpiresUtc != execution.LeaseFence.ExpiresUtc || job.LeaseExpiresUtc <= clock.GetUtcNow() ||
                job.Plan.ExpectedVersion != scope.ExpectedTenantVersion || job.Execution.PolicyKey != execution.PolicyKey || job.Execution.InventoryFingerprint != execution.InventoryFingerprint)
                throw new IdentityUnauthorizedException("Confirmed tenant purge authority is required.");
            var plan = await journal.GetPlanAsync(execution, job.Plan.PlanId, ct);
            if (plan?.ConsumedJobId != execution.JobId || plan.Plan.ConfirmationBinding != job.Plan.ConfirmationBinding)
                throw new IdentityUnauthorizedException("Confirmed tenant purge authority is required.");
            await execution.LeaseFence.RequireJournalAsync(ct);
        }
    }
}
internal sealed class IdentityPurgeFence(IIdentityTenantPurgeService identity, ManagementIdentityPurgeContext context) : IManagementTenantPurgeAccessFence
{
    public int ProtocolVersion => 1; public string Key => "ibeam-identity";
    public Task FenceOrReconcileAsync(ManagementTenantPurgeExecution e, string expectedTenantVersion, CancellationToken ct = default)
        => context.Invoke(e, async () => {
            if (expectedTenantVersion != e.Authority.ResourceVersion) throw new ManagementOperationPreconditionException();
            try { await identity.FenceAsync(ManagementIdentityPurgeContext.Scope(e), ct); }
            catch (IdentityTenantPurgePendingException) { throw new ManagementTenantPurgePendingException(); }
        });
    public Task RequireFencedAsync(ManagementTenantPurgeExecution e, CancellationToken ct = default)
        => context.Invoke(e, () => identity.RequireFencedAsync(ManagementIdentityPurgeContext.Scope(e), ct));
}
internal sealed class IdentityPurgeHandler<T>(T consumer, IIdentityTenantPurgeService identity, ManagementIdentityPurgeContext context) : IManagementTenantPurgeHandler where T : class, IManagementTenantPurgeHandler
{
    public int ProtocolVersion => 1;
    private static ManagementTenantPurgeCategory[] NativeCategories() => [..IdentityTenantPurgeInventory.Keys.Select(x => new ManagementTenantPurgeCategory("identity-" + x, "identity-metadata", "delete")),
        new("identity-purge-tombstones", "identity-metadata", "retain")];
    public ManagementTenantPurgeDefinition Describe() {
        var app = ManagementTenantPurgeProtocol.Definition(consumer.Describe()); var native = NativeCategories();
        if (consumer.ProtocolVersion != 1 || app.Categories.Any(x => native.Any(n => n.Key == x.Key))) throw new ManagementOperationUnavailableException();
        var key = "identity-v1-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(app.HandlerKey)))[..16].ToLowerInvariant();
        return ManagementTenantPurgeProtocol.Definition(app with { HandlerKey = key, Categories = app.Categories.Concat(native).ToArray() });
    }
    public async Task PreviewAsync(ManagementTenantPurgeExecution e, CancellationToken ct = default) {
        await identity.PreviewAsync(e.Authority.TenantId, ct); await consumer.PreviewAsync(e, ct);
    }
    public async Task PurgeOrReconcileAsync(ManagementTenantPurgeExecution e, CancellationToken ct = default) {
        await consumer.PurgeOrReconcileAsync(e, ct);
        await context.Invoke(e, async () => {
            try { await identity.CleanupAsync(ManagementIdentityPurgeContext.Scope(e), ct); }
            catch (IdentityTenantPurgePendingException) { throw new ManagementTenantPurgePendingException(); }
        });
    }
    public async Task<IReadOnlyList<ManagementTenantPurgeCategory>> VerifyAsync(ManagementTenantPurgeExecution e, CancellationToken ct = default) {
        var app = await consumer.VerifyAsync(e, ct);
        await context.Invoke(e, () => identity.VerifyAsync(ManagementIdentityPurgeContext.Scope(e), ct));
        return app.Concat(NativeCategories().Select(x => x with { Disposition = x.Disposition == "delete" ? "deleted" : "retained" })).ToArray();
    }
}
