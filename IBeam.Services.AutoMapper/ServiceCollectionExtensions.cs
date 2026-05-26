using System.Reflection;
using AutoMapper;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Services.AutoMapper
{
    //TODO: Extension may not work correctly with AddAutoMapper in some scenarios, verify usage.
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddIBeamAutoMapper(
            this IServiceCollection services,
            params Assembly[] profileAssemblies)
        {
            if (profileAssemblies == null || profileAssemblies.Length == 0)
                throw new ArgumentException("At least one assembly must be provided.", nameof(profileAssemblies));

            services.AddAutoMapper(cfg => cfg.AddMaps(profileAssemblies), profileAssemblies);

            services.AddScoped(typeof(IModelMapper<,>), typeof(AutoMapperModelMapper<,>));
            return services;
        }
    }
}
