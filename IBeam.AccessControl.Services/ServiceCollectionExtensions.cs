using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.AccessControl.Services;

public static class AccessControlServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamAccessControlServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AccessControlOptions>()
            .Bind(configuration.GetSection(AccessControlOptions.SectionName))
            .Validate(o =>
            {
                o.Validate();
                return true;
            })
            .ValidateOnStart();

        services.TryAddSingleton<IResourceAccessStore, InMemoryResourceAccessStore>();
        services.TryAddScoped<IResourceAccessService, ResourceAccessService>();
        services.TryAddScoped<IResourceAccessAuthorizer, ResourceAccessAuthorizer>();

        return services;
    }
}
