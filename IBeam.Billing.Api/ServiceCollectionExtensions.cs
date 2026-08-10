using IBeam.Billing.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Threading.RateLimiting;

namespace IBeam.Billing.Api;

public static class BillingApiServiceCollectionExtensions
{
    public const string PublicCheckoutRateLimitPolicy = "IBeamCommercePublic";

    public static IServiceCollection AddIBeamBillingApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddIBeamBillingServices(configuration);
        services.AddRateLimiter(options =>
            options.AddPolicy(PublicCheckoutRateLimitPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    })));
        services
            .AddControllers()
            .AddApplicationPart(typeof(BillingAdminController).GetTypeInfo().Assembly);
        return services;
    }
}
