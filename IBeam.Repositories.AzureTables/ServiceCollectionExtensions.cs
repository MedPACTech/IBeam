using IBeam.Repositories.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Repositories.AzureTables;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamAzureTablesRepositories(this IServiceCollection services)
    {
        services.TryAddSingleton<IAzureEntityKeyFormatter, AzureEntityKeyFormatter>();
        services.TryAddSingleton(typeof(IAzureEntityKeyResolver<>), typeof(AzureEntityKeyResolver<>));
        services.AddScoped(typeof(IRepositoryStore<>), typeof(AzureTablesRepositoryStore<>));
        services.AddScoped(typeof(IAzureTablesRepositoryStore<>), typeof(AzureTablesRepositoryStore<>));
        services.AddScoped(typeof(IAzureTablesRepositoryAsync<>), typeof(AzureTablesRepositoryAsync<>));
        services.AddScoped(typeof(IBaseRepositoryAsync<>), typeof(AzureTablesRepositoryAsync<>));
        services.AddScoped(typeof(IBaseRepository<>), typeof(AzureTablesRepositoryAsync<>));
        services.TryAddSingleton(typeof(IEntityKeyBinder<>), typeof(GuidRowKeyEntityKeyBinder<>));
        services.TryAddSingleton<IEntityLocator, NullEntityLocator>();
        return services;
    }

    public static IServiceCollection ConfigureIBeamAzureTables(
        this IServiceCollection services,
        Action<AzureTablesOptions> configure)
    {
        services.Configure(configure);
        services.PostConfigure<AzureTablesOptions>(options =>
        {
            options.GuidKeyFormat = AzureEntityKeyFormatter.NormalizeGuidFormat(options.GuidKeyFormat);
        });
        return services;
    }

    public static IServiceCollection ConfigureIBeamAzureTables(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AzureTablesOptions>(configuration.GetSection(AzureTablesOptions.SectionName));
        services.PostConfigure<AzureTablesOptions>(options =>
        {
            options.ConnectionString = ResolveConnectionString(configuration, options.ConnectionString);
            options.GuidKeyFormat = AzureEntityKeyFormatter.NormalizeGuidFormat(options.GuidKeyFormat);
        });
        return services;
    }

    private static string ResolveConnectionString(IConfiguration configuration, string? scopedConnectionString)
    {
        // Precedence:
        // 1) IBeam:Repositories:AzureTables:ConnectionString (bound into options)
        // 2) IBeam:AzureTables
        // 3) IBeam:ConnectionString
        // 4) ConnectionStrings:AzureTables
        // 5) ConnectionStrings:AzureStorage
        // 6) ConnectionStrings:IBeam
        // 7) ConnectionStrings:DefaultConnection
        var resolved =
            FirstNonEmpty(
                scopedConnectionString,
                configuration["IBeam:AzureTables"],
                configuration["IBeam:ConnectionString"],
                configuration.GetConnectionString("AzureTables"),
                configuration.GetConnectionString("AzureTable"),
                configuration.GetConnectionString("AzureStorage"),
                configuration.GetConnectionString("IBeam"),
                configuration.GetConnectionString("DefaultConnection"));

        if (string.IsNullOrWhiteSpace(resolved))
            throw new InvalidOperationException(
                "AzureTables connection string is required. Set IBeam:Repositories:AzureTables:ConnectionString, " +
                "or IBeam:AzureTables, or IBeam:ConnectionString, or ConnectionStrings:AzureTables/AzureStorage/IBeam/DefaultConnection.");

        return resolved!;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

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
            TableName = typeof(T).Name
        };

        configure(options);

        if (string.IsNullOrWhiteSpace(options.TableName))
            throw new InvalidOperationException($"{typeof(T).Name} mapping requires a non-empty TableName.");
        services.AddSingleton(options);
        return services;
    }

    public static IServiceCollection AddInMemoryEntityLocator(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IEntityLocator, InMemoryEntityLocator>());
        return services;
    }
}
