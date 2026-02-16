namespace IBeam.Communications.Email.Templating;

public static class EmailTemplateServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamEmailTemplating(this IServiceCollection services)
    {
        services.TryAddScoped<ITemplatedEmailService, TemplatedEmailService>();
        return services;
    }

    public static IServiceCollection AddIBeamEmailTemplating<TTemplateRenderer>(
        this IServiceCollection services)
        where TTemplateRenderer : class, IEmailTemplateRenderer
    {
        services.AddSingleton<IEmailTemplateRenderer, TTemplateRenderer>();
        services.AddScoped<ITemplatedEmailService, TemplatedEmailService>();
        return services;
    }
}
