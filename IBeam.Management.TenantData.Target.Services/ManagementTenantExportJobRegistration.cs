using IBeam.Management.Operations;
using IBeam.Management.Services;
using IBeam.Management.TenantData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IBeam.Management.TenantData.Target.Services;

public static class ManagementTenantExportJobRegistration
{
    private sealed class Registration;
    public static IServiceCollection AddIBeamManagementTenantExportJobs(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(x => x.ServiceType == typeof(Registration))) return services;
        services.AddSingleton<Registration>(); services.AddIBeamManagementServices(); services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<ManagementTenantExportJobOptions>().Bind(configuration.GetSection(ManagementTenantExportJobOptions.SectionName))
            .Validate(x => x.IsValid(), "Bounded export retention/leases and a versioned 32-byte artifact protection key ring are required.").ValidateOnStart();
        services.TryAddScoped<IManagementTenantExportJobService, ManagementTenantExportJobService>();
        services.TryAddScoped<IManagementTenantExportWorker, ManagementTenantExportWorker>();
        services.TryAddScoped<IManagementTenantExportCustomerArtifacts, ManagementTenantExportCustomerArtifacts>();
        services.AddHostedService<Validate>(); services.AddHostedService<ManagementTenantExportBackgroundWorker>(); return services;
    }
    private sealed class Validate(IServiceScopeFactory scopes) : IHostedService
    {
        public Task StartAsync(CancellationToken ct)
        {
            using var scope = scopes.CreateScope(); var provider = scope.ServiceProvider;
            if (provider.GetRequiredService<IManagementTenantExportJobRepository>().ProtocolVersion != 1) throw new InvalidOperationException("A compatible tenant export journal is required.");
            _ = provider.GetRequiredService<IManagementTenantExportWorker>(); _ = provider.GetRequiredService<IManagementTenantExportCustomerArtifacts>();
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}

/// <summary>Optional bounded recovery scan. Queue-based schedulers can instead call the same trusted worker port.</summary>
internal sealed class ManagementTenantExportBackgroundWorker(IServiceScopeFactory scopes, IOptions<ManagementTenantExportJobOptions> jobs,
    IOptions<ManagementOperationTargetOptions> target, IOptions<ManagementDiagnosticsOptions> deployment, TimeProvider clock) : BackgroundService
{
    private static readonly System.Diagnostics.Metrics.Meter Meter = new("IBeam.Management.TenantExport");
    private static readonly System.Diagnostics.Metrics.Counter<long> Failures = Meter.CreateCounter<long>("management.tenant-export.scan-failures");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!jobs.Value.EnableBackgroundWorker) return;
        string? cursor = null;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(jobs.Value.ScanIntervalSeconds), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope(); var provider = scope.ServiceProvider;
                var store = provider.GetRequiredService<IManagementTenantExportJobRepository>(); var worker = provider.GetRequiredService<IManagementTenantExportWorker>();
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken); budget.CancelAfter(TimeSpan.FromSeconds(55));
                // Include historical bindings for retention cleanup; starting/resuming
                // export work still requires the current explicit trusted binding.
                var page = await store.ListPageAsync(new(deployment.Value.ApplicationKey, deployment.Value.Environment, Guid.Empty, Guid.Empty, target.Value.RealmId),
                    10, cursor, budget.Token);
                cursor = page.ContinuationToken;
                foreach (var execution in page.Items)
                {
                  try {
                    var row = await store.GetAsync(execution, budget.Token); if (row is null) continue;
                    if (!row.Cleaned && !row.NeedsReconciliation && (row.CancelRequested || row.State == ManagementTenantExportStates.Failed || row.ExpiresUtc <= clock.GetUtcNow()))
                        await worker.CleanupAsync(execution, budget.Token);
                    else if (row.State is ManagementTenantExportStates.Requested or ManagementTenantExportStates.Running && !row.NeedsReconciliation)
                        await worker.RunAsync(execution, ct: budget.Token);
                  }
                  catch (ManagementAccessDeniedException) { }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch { Failures.Add(1); /* Retry only the bounded metadata scan; no provider text is logged. */ }
        }
    }
}
