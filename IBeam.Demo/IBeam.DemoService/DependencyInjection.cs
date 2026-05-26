using IBeam.DemoService.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.DemoService;

public static class DependencyInjection
{
    public static IServiceCollection AddDemoService(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<
            global::IBeam.DemoService.Services.IDemoService,
            global::IBeam.DemoService.Services.DemoService>();

        services.AddDemoIdentity(configuration);

        return services;
    }
}
