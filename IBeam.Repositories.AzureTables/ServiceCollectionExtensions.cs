using IBeam.Repositories.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Repositories.AzureTables;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamAzureTablesRepositories(this IServiceCollection services)
    {
        services.AddScoped(typeof(IRepositoryStore<>), typeof(AzureTablesRepositoryStore<>));
        services.AddScoped(typeof(IBaseRepository<>), typeof(AzureTablesRepositoryAsync<>));
        return services;
    }

    public static IServiceCollection ConfigureIBeamAzureTables(
        this IServiceCollection services,
        Action<AzureTablesOptions> configure)
    {
        services.Configure(configure);
        return services;
    }
}
