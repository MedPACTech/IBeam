using System.Text.RegularExpressions;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace IBeam.Management.Repositories.AzureTable;

public static class ManagementCredentialRepositoryServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamManagementAzureTableCredentialStore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ManagementCredentialStorageOptions>()
            .Bind(configuration.GetSection(ManagementCredentialStorageOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString), "A dedicated management credential connection is required.")
            .Validate(o => Regex.IsMatch(o.TableName ?? "", "^[A-Za-z][A-Za-z0-9]{2,62}$"), "A valid management credential table name is required.")
            .ValidateOnStart();
        services.TryAddScoped<IManagementApiCredentialRepository>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<ManagementCredentialStorageOptions>>().Value;
            return new AzureTableManagementApiCredentialRepository(new TableClient(options.ConnectionString, options.TableName));
        });
        return services;
    }

    /// <summary>Supply a dedicated TableClient with a managed identity/token credential or table-scoped SAS. No global customer client is replaced.</summary>
    public static IServiceCollection AddIBeamManagementAzureTableCredentialStore(this IServiceCollection services, TableClient managementCredentialTable)
    {
        ArgumentNullException.ThrowIfNull(managementCredentialTable);
        services.TryAddScoped<IManagementApiCredentialRepository>(_ => new AzureTableManagementApiCredentialRepository(managementCredentialTable));
        return services;
    }
}
