namespace IBeam.Ai.Completions;

/// <summary>
/// Outbound model invocation for IBeam-backed applications. Consumers declare which
/// model, at what effort, with what budget in configuration (IBeam:Ai profiles) and
/// inject this service; retries, refusals, structured output, usage accounting, and
/// credit metering are handled underneath.
/// </summary>
public interface IAiCompletionService
{
    Task<AiCompletionResult> CompleteAsync(AiCompletionRequest request, CancellationToken ct = default);

    IAsyncEnumerable<AiCompletionChunk> StreamAsync(AiCompletionRequest request, CancellationToken ct = default);
}

/// <summary>
/// Vendor adapter contract. Provider packages (IBeam.Ai.Anthropic, ...) implement this
/// and register themselves; the core service routes by AiProviderOptions.Type.
/// </summary>
public interface IAiCompletionProvider
{
    /// <summary>Adapter key matched case-insensitively against AiProviderOptions.Type, e.g. "anthropic".</summary>
    string ProviderType { get; }

    Task<AiCompletionResult> CompleteAsync(ResolvedAiProfile profile, AiCompletionRequest request, CancellationToken ct = default);

    IAsyncEnumerable<AiCompletionChunk> StreamAsync(ResolvedAiProfile profile, AiCompletionRequest request, CancellationToken ct = default);
}
