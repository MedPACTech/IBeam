using IBeam.Management.Operations;
using IBeam.Management.Services;
using IBeam.Management.TenantData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IBeam.Management.TenantData.Target.Services;

public sealed class ManagementTenantPurgeJobOptions
{
    public const string SectionName = "IBeam:Management:TenantPurge:Jobs";
    public int LeaseSeconds { get; set; } = 45;
    public bool EnableBackgroundWorker { get; set; }
    public int ScanIntervalSeconds { get; set; } = 10;
    public bool IsValid() => LeaseSeconds is >= 10 and <= 120 && ScanIntervalSeconds is >= 1 and <= 300;
}
public static class ManagementTenantPurgeJobExtensions
{
    public static IServiceCollection AddIBeamManagementTenantPurgeJobs(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(x => x.ServiceType == typeof(Registration))) return services;
        services.AddSingleton<Registration>(); services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<ManagementTenantPurgeJobOptions>().Bind(configuration.GetSection(ManagementTenantPurgeJobOptions.SectionName))
            .Validate(o => o.IsValid(), "Purge jobs require a 10-120 second lease and a 1-300 second scan interval.").ValidateOnStart();
        services.TryAddScoped<IManagementTenantPurgeJobService, ManagementTenantPurgeJobService>();
        services.TryAddScoped<IManagementTenantPurgeWorker, ManagementTenantPurgeWorker>();
        services.AddHostedService<Validate>(); services.AddHostedService<ManagementTenantPurgeBackgroundWorker>();
        return services;
    }
    private sealed class Registration;
    private sealed class Validate(IServiceScopeFactory scopes, IHostEnvironment host, IOptions<ManagementTenantPurgeOptions> options) : IHostedService
    {
        public Task StartAsync(CancellationToken ct)
        {
            using var scope = scopes.CreateScope(); var p = scope.ServiceProvider;
            var repository = p.GetRequiredService<IManagementTenantPurgeJournalRepository>();
            if (!options.Value.Enabled || repository.ProtocolVersion != 1 || !repository.Durable && !(options.Value.AllowVolatileDevelopmentJobs && host.EnvironmentName is "Development" or "Testing") ||
                p.GetRequiredService<IManagementTenantPurgeJobService>() is not ManagementTenantPurgeJobService ||
                p.GetRequiredService<IManagementTenantPurgeHandler>().ProtocolVersion != 1 || p.GetRequiredService<IManagementTenantPurgePolicy>().ProtocolVersion != 1)
                throw new InvalidOperationException("The standard purge coordinator requires explicit enabling, compatible native providers and durable atomic journal storage.");
            try { _ = ManagementTenantPurgeJobService.FenceKeys(p.GetServices<IManagementTenantPurgeAccessFence>()); }
            catch (ManagementOperationUnavailableException) { throw new InvalidOperationException("Purge requires compatible, uniquely named native access/write fences."); }
            _ = p.GetRequiredService<IManagementTenantPurgeWorker>();
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
/// <summary>Optional bounded recovery scanner. Unknown work is never automatically resumed; queues may call the same worker directly.</summary>
public sealed class ManagementTenantPurgeBackgroundWorker(IServiceScopeFactory scopes, IOptions<ManagementTenantPurgeJobOptions> options,
    IOptions<ManagementDiagnosticsOptions> deployment, IOptions<ManagementOperationTargetOptions> target) : BackgroundService
{
    private readonly Dictionary<string, string?> cursors = new(StringComparer.Ordinal);
    private long failures;
    private int nextBinding;
    public long FailureCount => Interlocked.Read(ref failures);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.EnableBackgroundWorker) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.ScanIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested) {
            try { await ScanAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch { Interlocked.Increment(ref failures); }
            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) return; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
    public async Task ScanAsync(CancellationToken ct = default)
    {
        if (!options.Value.EnableBackgroundWorker || !target.Value.EnabledOperations.Contains(ManagementTenantDataOperations.Delete, StringComparer.Ordinal)) return;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(55));
        var count = 0;
        var bindings = target.Value.TrustedPortals.DistinctBy(x => (x.OrganizationId, x.ApplicationId)).ToArray();
        if (bindings.Length == 0) return;
        var binding = bindings[nextBinding++ % bindings.Length];
        {
            using var scope = scopes.CreateScope(); var p = scope.ServiceProvider;
            var store = p.GetRequiredService<IManagementTenantPurgeJournalRepository>(); var worker = p.GetRequiredService<IManagementTenantPurgeWorker>();
            var key = binding.OrganizationId.ToString("N") + binding.ApplicationId.ToString("N");
            var page = await store.ListPageAsync(new(deployment.Value.ApplicationKey, deployment.Value.Environment, binding.OrganizationId, binding.ApplicationId, target.Value.RealmId),
                10, cursors.GetValueOrDefault(key), budget.Token);
            cursors[key] = page.ContinuationToken;
            foreach (var execution in page.Items) {
                if (++count > 10) return;
                var row = await store.GetJobAsync(execution, budget.Token);
                if (row is null || row.Phase == "done" || row.NeedsReconciliation) continue;
                try { await worker.RunAsync(execution, ct: budget.Token); }
                catch (OperationCanceledException) when (budget.IsCancellationRequested) { throw; }
                catch { Interlocked.Increment(ref failures); }
            }
        }
    }
}
