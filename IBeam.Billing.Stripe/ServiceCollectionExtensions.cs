using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Stripe;

namespace IBeam.Billing.Stripe;

public static class StripeBillingServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamStripeBilling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<StripeBillingOptions>(configuration.GetSection(StripeBillingOptions.SectionName));
        services.TryAddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<StripeBillingOptions>>().Value;
            options.Validate();
            return new StripeClient(options.SecretKey);
        });
        services.TryAddSingleton<IStripeBillingApiClient, StripeBillingApiClient>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IBillingCheckoutGateway, StripeBillingCheckoutGateway>());
        return services;
    }
}
