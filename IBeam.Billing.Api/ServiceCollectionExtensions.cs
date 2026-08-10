using IBeam.Billing.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Security.Claims;
using System.Threading.RateLimiting;

namespace IBeam.Billing.Api;

public static class BillingApiServiceCollectionExtensions
{
    public const string PublicCheckoutRateLimitPolicy = "IBeamCommercePublic";
    public const string CommerceAdministrationPolicy = "IBeamCommerceAdministration";

    public static IServiceCollection AddIBeamBillingApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddIBeamBillingServices(configuration);
        services.AddAuthorization(options =>
            options.AddPolicy(CommerceAdministrationPolicy, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context =>
                    HasClaimValue(context.User, "Owner", ClaimTypes.Role, "role", "roles") ||
                    HasClaimValue(context.User, "Administrator", ClaimTypes.Role, "role", "roles") ||
                    HasClaimValue(context.User, "Admin", ClaimTypes.Role, "role", "roles") ||
                    HasClaimValue(context.User, "billing.commerce.admin", "permission", "permissions", "scope", "scp"));
            }));
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

    private static bool HasClaimValue(ClaimsPrincipal principal, string expected, params string[] claimTypes)
    {
        var acceptedTypes = claimTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return principal.Claims
            .Where(x => acceptedTypes.Contains(x.Type))
            .SelectMany(x => x.Value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Any(x => string.Equals(x.Trim('[', ']', '"'), expected, StringComparison.OrdinalIgnoreCase));
    }
}
