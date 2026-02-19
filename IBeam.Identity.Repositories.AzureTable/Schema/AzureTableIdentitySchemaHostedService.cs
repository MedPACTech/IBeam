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
            var schemaManager = scope.ServiceProvider.GetRequiredService<IIdentitySchemaManager>();

            await schemaManager.ApplyAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("AzureTable identity schema is ready.");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("AzureTable identity schema ensure was cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AzureTable identity schema ensure failed.");

            // Fail fast: if schema can't be ensured, running the app will produce confusing runtime errors.
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
