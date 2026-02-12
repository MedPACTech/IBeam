using IBeam.Identity.Services.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.DemoService.Identity;

public static class DemoIdentityServiceCollectionExtensions
{
    public static IServiceCollection AddDemoIdentity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Delegate identity composition to the platform identity layer
        services.AddIBeamIdentity(configuration);

        return services;
    }
}
