using IBeam.Communications.Email.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Communications.Email.Templating;

public static class EmailTemplateServiceCollectionExtensions
{
    // Register templating pipeline (renderer provided elsewhere)
    public static IServiceCollection AddIBeamEmailTemplating(this IServiceCollection services)
    {
        services.AddScoped<ITemplatedEmailService, TemplatedEmailService>();
        return services;
    }

    // Convenience: register renderer + templating in one call
    public static IServiceCollection AddIBeamEmailTemplating<TTemplateRenderer>(
        this IServiceCollection services)
        where TTemplateRenderer : class, IEmailTemplateRenderer
    {
        services.AddSingleton<IEmailTemplateRenderer, TTemplateRenderer>();
        services.AddScoped<ITemplatedEmailService, TemplatedEmailService>();
        return services;
    }
}
