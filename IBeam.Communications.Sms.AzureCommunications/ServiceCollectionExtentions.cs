using IBeam.Communications.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Communications.Sms.AzureCommunications;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamAzureCommunicationsSms(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<AzureCommunicationsSmsOptions>()
            .Configure(o => configuration
                .GetSection(AzureCommunicationsSmsOptions.SectionName)
                .Bind(o))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString), "ConnectionString is required.")
            .ValidateOnStart();

        services.AddSingleton<ISmsService, AzureCommunicationsSmsService>();

        return services;
    }
}
