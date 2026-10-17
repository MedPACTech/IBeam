using System.Text.RegularExpressions;
using Azure.Data.Tables;
using IBeam.Management.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace IBeam.Management.Repositories.AzureTable.Diagnostics;

public sealed class SystemErrorReadStorageOptions
{
    public const string SectionName = "IBeam:Management:Diagnostics:Errors:Storage";
    public string ConnectionString { get; set; } = "";
    // Explicit full physical name: never inherit an Identity/management table prefix.
    public string TableName { get; set; } = "";
    public override string ToString() => "System error read storage (connection redacted)";
}

public static class ManagementSystemErrorReadRepositoryServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamManagementAzureTableSystemErrorReader(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SystemErrorReadStorageOptions>().Bind(configuration.GetSection(SystemErrorReadStorageOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString) &&
                Regex.IsMatch(o.TableName ?? "", "^[A-Za-z][A-Za-z0-9]{2,62}$"),
                "An explicit diagnostics connection and full table name are required.")
            .ValidateOnStart();
        services.TryAddScoped<ISystemErrorReadRepository>(provider =>
        {
            var configured = provider.GetRequiredService<IOptions<SystemErrorReadStorageOptions>>().Value;
            return new AzureTableSystemErrorReadRepository(new TableClient(configured.ConnectionString, configured.TableName));
        });
        return services;
    }

    /// <summary>Supply a dedicated, read-only TableClient (including managed identity/SAS support) without replacing a customer client.</summary>
    public static IServiceCollection AddIBeamManagementAzureTableSystemErrorReader(this IServiceCollection services, TableClient errorTable)
    {
        ArgumentNullException.ThrowIfNull(errorTable);
        services.TryAddScoped<ISystemErrorReadRepository>(_ => new AzureTableSystemErrorReadRepository(errorTable));
        return services;
    }
}
