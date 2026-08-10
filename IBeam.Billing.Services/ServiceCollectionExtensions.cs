using IBeam.Services.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Billing.Services;

public static class BillingServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamBillingServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddIBeamServicePolicies();
        services.AddIBeamServiceAuditing(configuration);
        services.Configure<BillingOptions>(configuration.GetSection(BillingOptions.SectionName));

        services.TryAddSingleton<IBillingOfferCatalogProvider, ConfigurationBillingOfferCatalogProvider>();
        services.TryAddScoped<IBillingCheckoutGatewayResolver, BillingCheckoutGatewayResolver>();
        services.TryAddSingleton<IBillingStore, InMemoryBillingStore>();
        services.TryAddScoped<IBillingCustomerService, BillingCustomerService>();
        services.TryAddScoped<IBillingSubscriptionService, BillingSubscriptionService>();
        services.TryAddScoped<IBillingInvoiceService, BillingInvoiceService>();
        services.TryAddScoped<IBillingProviderEventService, BillingProviderEventService>();

        return services;
    }
}
