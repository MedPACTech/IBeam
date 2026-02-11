using System;

namespace IBeam.Communications.Email.Templating;

public static class FileEmailTemplatesExtensions
{
    public static IServiceCollection AddIBeamFileEmailTemplates(
        this IServiceCollection services,
        Action<EmailTemplateOptions> configure)
    {
        services.Configure(configure);
        services.AddSingleton<IEmailTemplateRenderer, FileEmailTemplateRenderer>();
        return services;
    }
}

