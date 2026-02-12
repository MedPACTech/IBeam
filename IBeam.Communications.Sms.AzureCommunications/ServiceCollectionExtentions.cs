using IBeam.Communications.Abstractions;
using IBeam.Communications.Sms.AzureCommunications;
using Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamAzureCommunicationsSms(
        this IServiceCollection services,
        Action<AzureCommunicationsSmsOptions> configure)
    {
        services.AddOptions<AzureCommunicationsSmsOptions>()
            .Configure(configure)
            .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString), "ConnectionString is required.")
            .ValidateOnStart();

        services.AddOptions<SmsOptions>(); // shared defaults

        services.AddSingleton<ISmsService, AzureCommunicationsSmsService>();
        return services;
    }
}
