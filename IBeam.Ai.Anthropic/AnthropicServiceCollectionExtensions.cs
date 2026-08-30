using IBeam.Ai.Completions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IBeam.Ai.Anthropic;

public static class AnthropicServiceCollectionExtensions
{
    /// <summary>Registers the Anthropic (Claude) adapter for IBeam:Ai providers of type "anthropic".</summary>
    public static IServiceCollection AddIBeamAiAnthropic(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAiCompletionProvider, AnthropicCompletionProvider>());
        return services;
    }
}
