using IBeam.Storage.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Storage.S3;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamS3BlobStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<S3BlobStorageOptions>()
            .Bind(configuration.GetSection(S3BlobStorageOptions.SectionName))
            .Validate(o =>
            {
                o.Validate();
                return true;
            })
            .ValidateOnStart();

        services.AddSingleton<IBlobStorageService, S3BlobStorageService>();
        return services;
    }
}
