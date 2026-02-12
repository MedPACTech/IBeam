using IBeam.Communications.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Communications.Email.Templating;

public static class EmailTemplateServiceCollectionExtensions
{
    // Register templating pipeline (renderer provided elsewhere)
    public static IServiceCollection AddIBeamEmailTemplating(this IServiceCollection services)
    {
        // Don’t overwrite if already registered (lets apps decorate/replace cleanly)
        services.TryAddScoped<ITemplatedEmailService, TemplatedEmailService>();
        return services;
    }

    // Convenience: register renderer + templating in one call
    public static IServiceCollection AddIBeamEmailTemplating<TTemplateRenderer>(this IServiceCollection services)
        where TTemplateRenderer : class, IEmailTemplateRenderer
    {
        services.TryAddSingleton<IEmailTemplateRenderer, TTemplateRenderer>();
        services.TryAddScoped<ITemplatedEmailService, TemplatedEmailService>();
        return services;
    }
}
