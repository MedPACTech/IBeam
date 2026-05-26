using IBeam.Communications.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Communications.Email.AzureCommunications;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIBeamAzureCommunicationsEmail(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<AzureCommunicationsEmailOptions>()
            .Configure(o => configuration
                .GetSection(AzureCommunicationsEmailOptions.SectionName)
                .Bind(o))
            .PostConfigure(o =>
            {
                o.ConnectionString = ResolveConnectionString(configuration, o.ConnectionString);
            })
            .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString), "ConnectionString is required.")
            .ValidateOnStart();

        services.AddSingleton<IEmailService, AzureCommunicationsEmailService>();

        return services;
    }

    private static string ResolveConnectionString(IConfiguration configuration, string? scopedConnectionString)
    {
        // Precedence:
        // 1) IBeam:Communications:Email:Providers:AzureCommunications:ConnectionString (bound into options)
        // 2) IBeam:AzureCommunications
        // 3) IBeam:ConnectionString
        // 4) ConnectionStrings:AzureCommunications
        // 5) ConnectionStrings:IBeam
        // 6) ConnectionStrings:DefaultConnection
        var resolved = FirstNonEmpty(
            scopedConnectionString,
            configuration["IBeam:AzureCommunications"],
            configuration["IBeam:ConnectionString"],
            configuration.GetConnectionString("AzureCommunications"),
            configuration.GetConnectionString("IBeam"),
            configuration.GetConnectionString("DefaultConnection"));

        if (string.IsNullOrWhiteSpace(resolved))
            throw new InvalidOperationException(
                "Azure Communications Email connection string is required. Set provider ConnectionString, " +
                "or IBeam:AzureCommunications, or IBeam:ConnectionString, or ConnectionStrings:AzureCommunications/IBeam/DefaultConnection.");

        return resolved!;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}
