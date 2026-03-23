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
            .Validate(o =>
            {
                o.Validate();
                return true;
            })
            .ValidateOnStart();

        services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();
        return services;
    }
}
