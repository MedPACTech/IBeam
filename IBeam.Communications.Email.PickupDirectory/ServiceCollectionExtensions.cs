using IBeam.Communications.Email.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Communications.Email.PickupDirectory;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamPickupDirectoryEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PickupDirectoryEmailOptions>()
            .Bind(configuration.GetSection(PickupDirectoryEmailOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.DirectoryPath), "DirectoryPath is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.DefaultFromAddress), "DefaultFromAddress is required.")
            .ValidateOnStart();

        services.AddSingleton<IEmailService, PickupDirectoryEmailService>();
        return services;
    }
}
