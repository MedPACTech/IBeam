using System.Text.RegularExpressions;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace IBeam.Management.Repositories.AzureTable;
public sealed class ManagementApplicationStorageOptions
{
    public const string SectionName = "IBeam:Management:Applications:Storage";
    public string ConnectionString { get; set; } = "";
    public string TableName { get; set; } = "ManagementApplications";
}
public static class ManagementApplicationStorageExtensions
{
    public static IServiceCollection AddIBeamManagementAzureTableApplicationStore(this IServiceCollection services, TableClient applicationTable)
    {
        ArgumentNullException.ThrowIfNull(applicationTable);
        services.TryAddScoped<IManagementApplicationRepository>(_ => new AzureTableManagementApplicationRepository(applicationTable));
        return services;
    }
    public static IServiceCollection AddIBeamManagementAzureTableApplicationStore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ManagementApplicationStorageOptions>()
            .Bind(configuration.GetSection(ManagementApplicationStorageOptions.SectionName))
            .Validate(x => !string.IsNullOrWhiteSpace(x.ConnectionString) &&
                Regex.IsMatch(x.TableName ?? "", "^[A-Za-z][A-Za-z0-9]{2,62}$"), "Dedicated application storage is required.").ValidateOnStart();
        services.TryAddScoped<IManagementApplicationRepository>(provider =>
        {
            var x = provider.GetRequiredService<IOptions<ManagementApplicationStorageOptions>>().Value;
            return new AzureTableManagementApplicationRepository(new TableClient(x.ConnectionString, x.TableName));
        });
        return services;
    }
}
