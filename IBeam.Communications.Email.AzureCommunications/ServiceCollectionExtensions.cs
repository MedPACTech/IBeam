using IBeam.Communications.Email.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Communications.Email.AzureCommunications;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamAzureCommunicationsEmail(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AzureCommunicationsEmailOptions>()
            .Bind(configuration.GetSection(AzureCommunicationsEmailOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString), "ConnectionString is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.DefaultFromAddress), "DefaultFromAddress is required.")
            .ValidateOnStart();

        services.AddSingleton<IEmailService, AzureCommunicationsEmailService>();
        return services;
    }
}
