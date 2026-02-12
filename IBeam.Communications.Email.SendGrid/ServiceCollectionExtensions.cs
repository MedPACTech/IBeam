using IBeam.Communications.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Communications.Email.SendGrid;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamSendGridEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SendGridEmailOptions>()
            .Bind(configuration.GetSection(SendGridEmailOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "ApiKey is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.DefaultFromAddress), "DefaultFromAddress is required.")
            .ValidateOnStart();

        services.AddSingleton<IEmailService, SendGridEmailService>();
        return services;
    }
}
