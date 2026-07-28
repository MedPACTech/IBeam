using Azure.Data.Tables;
using IBeam.Billing;
using IBeam.Credits;
using IBeam.Licensing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Commerce.Repositories.AzureTable;

public static class AzureTableCommerceServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamCommerceAzureTableStores(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AzureTableCommerceOptions>()
            .Bind(configuration.GetSection(AzureTableCommerceOptions.SectionName))
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
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureTableCommerceOptions>>().Value;
            options.Validate();
            return new TableServiceClient(options.StorageConnectionString);
        });

        services.TryAddSingleton<AzureTableCommerceStore>();
        services.Replace(ServiceDescriptor.Singleton<ILicensingStore>(sp => sp.GetRequiredService<AzureTableCommerceStore>()));
        services.Replace(ServiceDescriptor.Singleton<IBillingStore>(sp => sp.GetRequiredService<AzureTableCommerceStore>()));
        services.Replace(ServiceDescriptor.Singleton<ICreditReservationStore>(sp => sp.GetRequiredService<AzureTableCommerceStore>()));
        services.Replace(ServiceDescriptor.Singleton<ICreditLedgerStore>(sp => sp.GetRequiredService<AzureTableCommerceStore>()));
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
