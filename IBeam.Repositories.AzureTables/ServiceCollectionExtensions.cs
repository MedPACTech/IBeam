using IBeam.Repositories.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Repositories.AzureTables;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamAzureTablesRepositories(this IServiceCollection services)
    {
        services.AddScoped(typeof(IRepositoryStore<>), typeof(AzureTablesRepositoryStore<>));
        services.AddScoped(typeof(IAzureTablesRepositoryStore<>), typeof(AzureTablesRepositoryStore<>));
        services.AddScoped(typeof(IAzureTablesRepositoryAsync<>), typeof(AzureTablesRepositoryAsync<>));
        services.AddScoped(typeof(IBaseRepositoryAsync<>), typeof(AzureTablesRepositoryAsync<>));
        services.AddScoped(typeof(IBaseRepository<>), typeof(AzureTablesRepositoryAsync<>));
        services.TryAddSingleton<IEntityLocator, NullEntityLocator>();
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

    public static IServiceCollection AddAzureEntityMapping<T>(
        this IServiceCollection services,
        Action<AzureEntityMappingOptions<T>> configure)
        where T : class, IEntity
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new AzureEntityMappingOptions<T>
        {
            TableName = typeof(T).Name,
            WriteKey = (_, entity) => new AzureEntityKey
            {
                PartitionKey = "global",
                RowKey = entity.Id.ToString("N")
            }
        };

        configure(options);

        if (string.IsNullOrWhiteSpace(options.TableName))
            throw new InvalidOperationException($"{typeof(T).Name} mapping requires a non-empty TableName.");
        if (options.WriteKey is null)
            throw new InvalidOperationException($"{typeof(T).Name} mapping requires WriteKey.");

        services.AddSingleton(options);
        return services;
    }

    public static IServiceCollection AddInMemoryEntityLocator(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IEntityLocator, InMemoryEntityLocator>());
        return services;
    }
}
