using Azure.Data.Tables;
using IBeam.Api.Abstractions;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Services.Logging.AzureTable;

public static class AzureTableSystemLogServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamAzureTableSystemLogs(
        this IServiceCollection services,
        IConfiguration? configuration = null,
        Action<AzureTableSystemLogOptions>? configure = null,
        bool registerAuditTrailSink = true)
    {
        if (configuration is not null)
        {
            services.AddOptions<AzureTableSystemLogOptions>()
                .Bind(configuration.GetSection(AzureTableSystemLogOptions.SectionName))
                .PostConfigure(o =>
                {
                    o.StorageConnectionString = ResolveConnectionString(configuration, o.StorageConnectionString);
                    o.NormalizeAndValidate(requireConnectionString: false);
                });
        }
        else
        {
            services.AddOptions<AzureTableSystemLogOptions>();
        }

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AzureTableSystemLogOptions>>().Value;
            options.NormalizeAndValidate(requireConnectionString: true);
            return new TableServiceClient(options.StorageConnectionString);
        });

        services.AddScoped<AzureTableSystemLogSink>();
        services.Replace(ServiceDescriptor.Scoped<ISystemLogSink>(sp => sp.GetRequiredService<AzureTableSystemLogSink>()));

        if (registerAuditTrailSink)
        {
            services.Replace(ServiceDescriptor.Scoped<IAuditTrailSink>(sp => sp.GetRequiredService<AzureTableSystemLogSink>()));
        }

        return services;
    }

    private static string ResolveConnectionString(IConfiguration configuration, string? scopedConnectionString)
    {
        var resolved =
            FirstNonEmpty(
                scopedConnectionString,
                configuration["IBeam:AzureTables"],
                configuration["IBeam:Repositories:ConnectionString"],
                configuration["IBeam:ConnectionString"],
                configuration.GetConnectionString("AzureTables"),
                configuration.GetConnectionString("AzureTable"),
                configuration.GetConnectionString("AzureStorage"),
                configuration.GetConnectionString("IBeam"),
                configuration.GetConnectionString("DefaultConnection"));

        return resolved ?? string.Empty;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}
