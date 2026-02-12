using IBeam.Communications.Abstractions;
using IBeam.Communications.Email.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;

namespace IBeam.Communications.Email.Templating;

public static class FileEmailTemplatesExtensions
{
    public static IServiceCollection AddIBeamEmailTemplatingFromFiles(
        this IServiceCollection services,
        Action<EmailTemplateOptions> configure)
    {
        services.Configure(configure);

        // Renderer
        services.TryAddSingleton<IEmailTemplateRenderer, FileEmailTemplateRenderer>();

        // Orchestration (render + send)
        services.TryAddScoped<ITemplatedEmailService, TemplatedEmailService>();

        return services;
    }
}
