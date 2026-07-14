using IBeam.Communications.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Communications.Email.AzureCommunications;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamAzureCommunicationsEmail(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<AzureCommunicationsEmailOptions>()
            .Configure(o => configuration
                .GetSection(AzureCommunicationsEmailOptions.SectionName)
                .Bind(o))
            .Validate(
                o => AzureCommunicationsConnectionStringValidator.IsValid(o.ConnectionString),
                AzureCommunicationsConnectionStringValidator.FailureMessage)
            .ValidateOnStart();

        services.AddSingleton<IEmailService, AzureCommunicationsEmailService>();

        return services;
    }
}
