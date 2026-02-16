using IBeam.Communications.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Communications.Sms.AzureCommunications;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamAzureCommunicationsSms(
        this IServiceCollection services,
        Action<AzureCommunicationsSmsOptions> configure)
    {
        services.Configure(configure);

        // Ensure global SmsOptions exists
        services.AddOptions<SmsOptions>();

        // Provider registration
        services.AddSingleton<ISmsService, AzureCommunicationsSmsService>();

        return services;
    }
}
