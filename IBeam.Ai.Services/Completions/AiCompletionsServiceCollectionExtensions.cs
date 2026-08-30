using IBeam.Ai.Completions;
using IBeam.Ai.Services.Completions;
using IBeam.Licensing.Credits;
using IBeam.Credits;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IBeam.Ai;

public static class AiCompletionsServiceCollectionExtensions
{
    /// <summary>
    /// Registers IAiCompletionService driven by IBeam:Ai configuration profiles.
    /// Provider adapters register separately (e.g. AddIBeamAiAnthropic from
    /// IBeam.Ai.Anthropic). When ILicenseCreditGate is registered, metered profiles
    /// (CreditBucketKey set) reserve and settle credits around each call; without it,
    /// only unmetered profiles are usable.
    /// </summary>
    public static IServiceCollection AddIBeamAiCompletions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<AiOptions>()
            .Configure(o => configuration
                .GetSection(AiOptions.SectionName)
                .Bind(o))
            .Validate(o => o.Validate(), "Invalid IBeam:Ai options")
            .ValidateOnStart();

        services.AddScoped<AiCompletionService>();
        services.AddScoped<IAiCompletionService>(sp =>
        {
            var inner = sp.GetRequiredService<AiCompletionService>();
            var gate = sp.GetService<ILicenseCreditGate>();
            if (gate is null)
                return inner;

            return new CreditedAiCompletionService(
                inner,
                gate,
                sp.GetRequiredService<ICreditPolicyService>(),
                sp.GetRequiredService<ICreditReservationService>());
        });

        return services;
    }
}
