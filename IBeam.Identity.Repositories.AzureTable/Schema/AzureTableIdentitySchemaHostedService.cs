using IBeam.Identity.Abstractions.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IBeam.Identity.Repositories.AzureTable.Schema;

internal sealed class AzureTableIdentitySchemaHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AzureTableIdentitySchemaHostedService> _logger;

    public AzureTableIdentitySchemaHostedService(
        IServiceProvider serviceProvider,
        ILogger<AzureTableIdentitySchemaHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Ensuring AzureTable identity schema...");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            _logger.LogInformation("DI scope created.");
            var schemaManager = scope.ServiceProvider.GetRequiredService<IIdentitySchemaManager>();
            _logger.LogInformation("IIdentitySchemaManager resolved.");

            _logger.LogInformation("Checking AzureTable identity schema status...");

            var applyTask = schemaManager.ApplyAsync(cancellationToken);
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30)); // No cancellation token here

            var completedTask = await Task.WhenAny(applyTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                _logger.LogError("AzureTable identity schema ensure timed out after 30 seconds.");
                throw new TimeoutException("AzureTable identity schema ensure timed out.");
            }

            await applyTask; // Propagate exceptions if any

            _logger.LogInformation("AzureTable identity schema is ready.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("AzureTable identity schema ensure was cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during DI scope or schema manager resolution.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
