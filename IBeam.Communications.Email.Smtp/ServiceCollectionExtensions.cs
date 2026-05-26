using IBeam.Communications.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;


namespace IBeam.Communications.Email.Smtp;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamSmtpEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SmtpEmailOptions>()
            .Bind(configuration.GetSection(SmtpEmailOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Host), "SMTP Host is required.")
            .Validate(o => o.Port > 0 && o.Port <= 65535, "SMTP Port must be a valid TCP port.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.DefaultFromAddress), "DefaultFromAddress is required.")
            .ValidateOnStart();

        // Singleton is OK because it only holds options and uses new SmtpClient per send.
        services.AddSingleton<IEmailService, SmtpEmailService>();

        return services;
    }
}
