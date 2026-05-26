using IBeam.Repositories.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Repositories.OrmLite;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamOrmLiteRepositories(this IServiceCollection services)
    {
        services.AddScoped(typeof(IRepositoryStore<>), typeof(OrmLiteRepositoryStore<>));
        services.AddScoped(typeof(IBaseRepository<>), typeof(OrmLiteRepositoryAsync<>));
        return services;
    }
}
