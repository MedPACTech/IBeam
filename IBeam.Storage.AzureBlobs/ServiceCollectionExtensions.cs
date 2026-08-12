using IBeam.Storage.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Storage.AzureBlobs;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamAzureBlobStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AzureBlobStorageOptions>()
            .Bind(configuration.GetSection(AzureBlobStorageOptions.SectionName))
            .PostConfigure(o =>
            {
                if (string.IsNullOrWhiteSpace(o.ServiceUri))
                    o.ConnectionString = ResolveConnectionString(configuration, o.ConnectionString);
            })
            .Validate(o =>
            {
                o.Validate();
                return true;
            })
            .ValidateOnStart();

        services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();
        return services;
    }

    private static string? ResolveConnectionString(IConfiguration configuration, string? scopedConnectionString)
    {
        var resolved =
            FirstNonEmpty(
                scopedConnectionString,
                configuration["IBeam:AzureStorage"],
                configuration["IBeam:Storage:ConnectionString"],
                configuration["IBeam:ConnectionString"],
                configuration.GetConnectionString("AzureBlobs"),
                configuration.GetConnectionString("AzureBlobStorage"),
                configuration.GetConnectionString("AzureStorage"),
                configuration.GetConnectionString("IBeam"),
                configuration.GetConnectionString("DefaultConnection"));

        return resolved;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}
