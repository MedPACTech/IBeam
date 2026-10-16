using System.Text.RegularExpressions;
using Azure.Data.Tables;
using IBeam.Management.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace IBeam.Management.Repositories.AzureTable.Diagnostics;

public sealed class SystemLogReadStorageOptions
{
    public const string SectionName = "IBeam:Management:Diagnostics:Logs:Storage";
    public string ConnectionString { get; set; } = "";
    // Explicit full physical name: never inherit an Identity/management or logging table prefix.
    public string TableName { get; set; } = "";
    public override string ToString() => "System log read storage (connection redacted)";
}

public static class ManagementSystemLogReadRepositoryServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamManagementAzureTableSystemLogReader(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SystemLogReadStorageOptions>().Bind(configuration.GetSection(SystemLogReadStorageOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString) &&
                Regex.IsMatch(o.TableName ?? "", "^[A-Za-z][A-Za-z0-9]{2,62}$"),
                "An explicit diagnostics connection and full table name are required.")
            .ValidateOnStart();
        services.TryAddScoped<ISystemLogReadRepository>(provider =>
        {
            var configured = provider.GetRequiredService<IOptions<SystemLogReadStorageOptions>>().Value;
            return new AzureTableSystemLogReadRepository(new TableClient(configured.ConnectionString, configured.TableName));
        });
        return services;
    }

    /// <summary>Supply a dedicated, read-only TableClient (including managed identity/SAS support) without replacing a customer client.</summary>
    public static IServiceCollection AddIBeamManagementAzureTableSystemLogReader(this IServiceCollection services, TableClient logTable)
    {
        ArgumentNullException.ThrowIfNull(logTable);
        services.TryAddScoped<ISystemLogReadRepository>(_ => new AzureTableSystemLogReadRepository(logTable));
        return services;
    }
}
