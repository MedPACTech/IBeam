using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using IBeam.Communications.Abstractions.Options;

namespace IBeam.Communications.Abstractions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamCommunications(
        this IServiceCollection services, 
        IConfiguration configuration)
    {

        // TODO: use IValidateOptions<T> for more complex validation logic
        // services.AddSingleton<IValidateOptions<EmailDefaultsOptions>, EmailDefaultsOptionsValidator>();
        // services.AddSingleton<IValidateOptions<SmsOptions>, SmsDefaultsOptionsValidator>();

        //TODO: Sms or Email is optional - we should allow for one or the other to be configured, but not require both.
        //This will require some custom validation logic.
        services
        .AddOptions<EmailTemplateOptions>()
          .Configure(o => configuration
            .GetSection(EmailTemplateOptions.SectionName)
            .Bind(o));

        services
        .AddOptions<EmailOptions>()
          .Configure(o => configuration
            .GetSection(EmailOptions.SectionName)
            .Bind(o))
          .Validate(o => o.Validate(), "Invalid EmailOptions options")
          .ValidateOnStart();

        services
            .AddOptions<SmsOptions>()
              .Configure(o => configuration
                .GetSection(SmsOptions.SectionName)
                .Bind(o))
              .Validate(o => o.Validate(), "Invalid SMS options")
              .ValidateOnStart();

        services.AddScoped<IEmailTemplateRenderer, FileSystemEmailTemplateRenderer>();
        services.AddScoped<ITemplatedEmailService, TemplatedEmailService>();

        return services;
    }

    /// <summary>
    /// Registers the provider-neutral inbound-SMS STOP/START/HELP compliance pipeline.
    /// The host app must also register an ISmsConsentStore implementation (where its
    /// consent state lives) and a provider webhook adapter such as
    /// AddIBeamCommunicationsSmsAzureInbound to parse the provider payload.
    /// </summary>
    public static IServiceCollection AddIBeamSmsInboundCompliance(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<SmsInboundOptions>()
              .Configure(o => configuration
                .GetSection(SmsInboundOptions.SectionName)
                .Bind(o))
              .Validate(o => o.Validate(), "Invalid SMS inbound options: set BrandName (and SupportContact for the HELP reply) or override all three reply texts")
              .ValidateOnStart();

        services.AddScoped<ISmsInboundProcessor, SmsInboundProcessor>();

        return services;
    }
}
