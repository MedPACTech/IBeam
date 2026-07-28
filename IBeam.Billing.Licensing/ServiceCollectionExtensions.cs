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

        services.TryAddScoped<IBillingLicenseReconciler, BillingLicenseReconciler>();
        return services;
    }
}
