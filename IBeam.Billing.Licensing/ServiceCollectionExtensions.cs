using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Billing.Licensing;

public static class BillingLicensingServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamBillingLicenseReconciliation(
        this IServiceCollection services,
        Action<BillingLicenseReconciliationOptions>? configure = null)
    {
        if (configure is not null)
            services.Configure(configure);
        else
            services.AddOptions<BillingLicenseReconciliationOptions>();
        services.AddOptions<BillingPurchaseClaimOptions>();

        services.TryAddScoped<IBillingLicenseReconciler, BillingLicenseReconciler>();
        services.TryAddScoped<IBillingProviderMigrationService, BillingProviderMigrationService>();
        services.TryAddScoped<ICommerceAdministrationService, CommerceAdministrationService>();
        services.TryAddScoped<BillingPurchaseLicenseFulfillmentService>();
        services.TryAddScoped<IBillingPurchaseLicenseFulfillmentService>(
            sp => sp.GetRequiredService<BillingPurchaseLicenseFulfillmentService>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IBillingPaidPurchaseHandler>(
                sp => sp.GetRequiredService<BillingPurchaseLicenseFulfillmentService>()));
        services.TryAddSingleton<IBillingPurchaseClaimStore, InMemoryBillingPurchaseClaimStore>();
        services.TryAddScoped<IBillingPurchaseClaimService, BillingPurchaseClaimService>();
        return services;
    }
}
