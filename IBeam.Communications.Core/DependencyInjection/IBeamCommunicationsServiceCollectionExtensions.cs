using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using IBeam.Communications.Core.Options;
using IBeam.Communications.Core.Validation;

namespace IBeam.Communications.Core.DependencyInjection;

public static class IBeamCommunicationsCoreServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamCommunicationsCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<EmailDefaultsOptions>, EmailDefaultsOptionsValidator>();
        services.AddSingleton<IValidateOptions<SmsDefaultsOptions>, SmsDefaultsOptionsValidator>();

        services.Configure<EmailDefaultsOptions>(
            configuration.GetSection(EmailDefaultsOptions.SectionName).Bind);

        services.Configure<SmsDefaultsOptions>(
            configuration.GetSection(SmsDefaultsOptions.SectionName).Bind);

        // Force validation at startup even when using Configure(...)
        services.AddOptions<EmailDefaultsOptions>().ValidateOnStart();
        services.AddOptions<SmsDefaultsOptions>().ValidateOnStart();

        return services;
    }
}
