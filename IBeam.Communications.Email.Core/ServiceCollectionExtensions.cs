using IBeam.Communications.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Communications.Email.Templating.Files;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamEmailTemplatingFromFiles(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<EmailTemplateOptions>()
            .Configure(o => configuration
                .GetSection(EmailTemplateOptions.SectionName)
                .Bind(o))
            .Validate(o => !string.IsNullOrWhiteSpace(o.BasePath), "BasePath is required.")
            .ValidateOnStart();

        // Renderer (templates -> rendered bodies)
        services.TryAddSingleton<IEmailTemplateRenderer, FileEmailTemplateRenderer>();

        // Orchestration (render + send)
        services.TryAddScoped<ITemplatedEmailService, TemplatedEmailService>();

        return services;
    }
}
