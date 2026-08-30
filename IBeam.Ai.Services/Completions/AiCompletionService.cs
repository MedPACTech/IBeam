using System.Runtime.CompilerServices;
using IBeam.Ai.Completions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IBeam.Ai.Services.Completions;

public sealed class AiCompletionService : IAiCompletionService
{
    private readonly AiOptions _options;
    private readonly IReadOnlyDictionary<string, IAiCompletionProvider> _providers;
    private readonly ILogger<AiCompletionService> _logger;

    public AiCompletionService(
        IOptions<AiOptions> options,
        IEnumerable<IAiCompletionProvider> providers,
        ILogger<AiCompletionService> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _providers = (providers ?? []).ToDictionary(
            p => p.ProviderType,
            p => p,
            StringComparer.OrdinalIgnoreCase);
    }

    public ResolvedAiProfile ResolveProfile(AiCompletionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var profileName = string.IsNullOrWhiteSpace(request.Profile) ? _options.DefaultProfile : request.Profile;
        if (string.IsNullOrWhiteSpace(profileName))
            throw new InvalidOperationException("No profile was requested and IBeam:Ai:DefaultProfile is not set.");
        if (!_options.Profiles.TryGetValue(profileName, out var profile))
            throw new InvalidOperationException($"AI profile '{profileName}' is not declared in IBeam:Ai:Profiles.");
        if (!_options.Providers.TryGetValue(profile.Provider, out var provider))
            throw new InvalidOperationException($"AI profile '{profileName}' references provider '{profile.Provider}' which is not declared in IBeam:Ai:Providers.");

        return new ResolvedAiProfile(profileName, provider, profile);
    }

    public async Task<AiCompletionResult> CompleteAsync(AiCompletionRequest request, CancellationToken ct = default)
    {
        var resolved = ResolveProfile(request);
        if (!TryGetAdapter(resolved, out var adapter, out var notConfigured))
            return notConfigured!;

        var attempts = _options.MaxRetryAttempts + 1;
        AiCompletionResult result = AiCompletionResult.Fail(AiCompletionStatus.Unavailable, "Not attempted.");
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                result = await adapter.CompleteAsync(resolved, request, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI completion attempt {Attempt}/{Attempts} threw for profile {Profile}.",
                    attempt, attempts, resolved.ProfileName);
                result = AiCompletionResult.Fail(AiCompletionStatus.Unavailable, ex.Message);
            }

            // Only transient failures are retried; a refusal retried in a loop is a bug.
            if (result.Status != AiCompletionStatus.Unavailable || attempt == attempts)
                return result;

            var delay = TimeSpan.FromMilliseconds(_options.RetryBaseDelayMilliseconds * Math.Pow(2, attempt - 1));
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, ct).ConfigureAwait(false);
        }

        return result;
    }

    public async IAsyncEnumerable<AiCompletionChunk> StreamAsync(
        AiCompletionRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var resolved = ResolveProfile(request);
        if (!TryGetAdapter(resolved, out var adapter, out var notConfigured))
        {
            yield return AiCompletionChunk.Final(notConfigured!.Status, AiUsage.Empty, notConfigured.ErrorMessage);
            yield break;
        }

        // No mid-stream retry: once chunks have been emitted a retry would duplicate output.
        await foreach (var chunk in adapter.StreamAsync(resolved, request, ct).ConfigureAwait(false))
            yield return chunk;
    }

    private bool TryGetAdapter(ResolvedAiProfile resolved, out IAiCompletionProvider adapter, out AiCompletionResult? notConfigured)
    {
        if (!_providers.TryGetValue(resolved.Provider.Type, out adapter!))
        {
            notConfigured = AiCompletionResult.Fail(
                AiCompletionStatus.NotConfigured,
                $"No IAiCompletionProvider is registered for provider type '{resolved.Provider.Type}'. Reference and register the matching adapter package (e.g. IBeam.Ai.Anthropic).");
            return false;
        }

        if (!resolved.IsConfigured)
        {
            notConfigured = AiCompletionResult.Fail(
                AiCompletionStatus.NotConfigured,
                $"Provider '{resolved.Profile.Provider}' has no ApiKey configured.");
            return false;
        }

        notConfigured = null;
        return true;
    }
}
