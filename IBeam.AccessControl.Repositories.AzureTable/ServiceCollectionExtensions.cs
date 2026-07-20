using Azure.Data.Tables;
using IBeam.AccessControl;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.AccessControl.Repositories.AzureTable;

public static class AzureTableAccessControlServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamAccessControlAzureTableStores(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AzureTableAccessControlOptions>()
            .Bind(configuration.GetSection(AzureTableAccessControlOptions.SectionName))
            .PostConfigure(o =>
            {
                o.StorageConnectionString = ResolveConnectionString(configuration, o.StorageConnectionString);
            })
            .Validate(o =>
            {
                o.Validate();
                return true;
            })
            .ValidateOnStart();

        services.TryAddSingleton(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureTableAccessControlOptions>>().Value;
            options.Validate();
            return new TableServiceClient(options.StorageConnectionString);
        });

        services.Replace(ServiceDescriptor.Singleton<IResourceAccessStore, AzureTableResourceAccessStore>());
        services.Replace(ServiceDescriptor.Singleton<IPermissionRoleMapStore, AzureTablePermissionRoleMapStore>());
        services.Replace(ServiceDescriptor.Singleton<IServiceOperationPermissionStore, AzureTableServiceOperationPermissionStore>());
        return services;
    }

    private static string ResolveConnectionString(IConfiguration configuration, string? scopedConnectionString)
    {
        var resolved =
            FirstNonEmpty(
                scopedConnectionString,
                configuration["IBeam:AzureTables"],
                configuration["IBeam:Repositories:ConnectionString"],
                configuration["IBeam:ConnectionString"],
                configuration.GetConnectionString("AzureTables"),
                configuration.GetConnectionString("AzureTable"),
                configuration.GetConnectionString("AzureStorage"),
                configuration.GetConnectionString("IBeam"),
                configuration.GetConnectionString("DefaultConnection"));

        return resolved ?? string.Empty;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}
