using IBeam.Repositories.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Repositories.AzureTables;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamAzureTablesRepositories(this IServiceCollection services)
    {
        services.AddScoped(typeof(IRepositoryStore<>), typeof(AzureTablesRepositoryStore<>));
        services.AddScoped(typeof(IAzureTablesRepositoryStore<>), typeof(AzureTablesRepositoryStore<>));
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

    public static IServiceCollection ConfigureIBeamAzureTables(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AzureTablesOptions>(configuration.GetSection(AzureTablesOptions.SectionName));
        return services;
    }

    public static IServiceCollection AddAzureTablePartitionKeyStrategy<T>(
        this IServiceCollection services,
        IAzureTablePartitionKeyStrategy<T> strategy)
        where T : class, IEntity
    {
        ArgumentNullException.ThrowIfNull(strategy);
        services.AddSingleton(strategy);
        return services;
    }

    public static IServiceCollection AddAzureTablePartitionKeyStrategy<T, TStrategy>(this IServiceCollection services)
        where T : class, IEntity
        where TStrategy : class, IAzureTablePartitionKeyStrategy<T>
    {
        services.AddSingleton<IAzureTablePartitionKeyStrategy<T>, TStrategy>();
        return services;
    }

    public static IServiceCollection AddAzureTablePartitionKeyStrategy<T>(
        this IServiceCollection services,
        Func<IServiceProvider, IAzureTablePartitionKeyStrategy<T>> factory)
        where T : class, IEntity
    {
        ArgumentNullException.ThrowIfNull(factory);
        services.AddSingleton<IAzureTablePartitionKeyStrategy<T>>(factory);
        return services;
    }

    public static IServiceCollection UseGlobalPartitionKey<T>(
        this IServiceCollection services,
        string partitionKey = "global")
        where T : class, IEntity
        => services.AddAzureTablePartitionKeyStrategy<T>(AzureTablePartitionKeyStrategies.Global<T>(partitionKey));

    public static IServiceCollection UseTenantPartitionKey<T>(this IServiceCollection services)
        where T : class, IEntity
        => services.AddAzureTablePartitionKeyStrategy<T>(AzureTablePartitionKeyStrategies.Tenant<T>());

    public static IServiceCollection UseTenantHashBucketPartitionKey<T>(
        this IServiceCollection services,
        int bucketCount = 16)
        where T : class, IEntity
        => services.AddAzureTablePartitionKeyStrategy<T>(AzureTablePartitionKeyStrategies.TenantHashBucket<T>(bucketCount));
}
