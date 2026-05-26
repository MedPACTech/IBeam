using IBeam.Storage.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Storage.FileSystem;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamFileSystemBlobStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FileSystemBlobStorageOptions>()
            .Bind(configuration.GetSection(FileSystemBlobStorageOptions.SectionName))
            .Validate(o =>
            {
                o.Validate();
                return true;
            })
            .ValidateOnStart();

        services.AddSingleton<IBlobStorageService, FileSystemBlobStorageService>();
        return services;
    }
}
