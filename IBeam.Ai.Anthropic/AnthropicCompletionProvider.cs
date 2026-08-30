using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using IBeam.Ai.Completions;

namespace IBeam.Ai.Anthropic;

public sealed class AnthropicCompletionProvider : IAiCompletionProvider
{
    public const string ProviderTypeKey = "anthropic";

    private readonly ConcurrentDictionary<string, AnthropicClient> _clients = new(StringComparer.Ordinal);

    public string ProviderType => ProviderTypeKey;

    public async Task<AiCompletionResult> CompleteAsync(
        ResolvedAiProfile profile,
        AiCompletionRequest request,
        CancellationToken ct = default)
    {
        var client = GetClient(profile.Provider);
        var parameters = BuildParameters(profile, request);

        Message response;
        try
        {
            response = await client.Messages.Create(parameters, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return AiCompletionResult.Fail(ClassifyException(ex), ex.Message);
        }

        var usage = MapUsage(response.Usage);
        var text = string.Concat(response.Content
            .Select(b => b.Value)
            .OfType<TextBlock>()
            .Select(b => b.Text));

        if (IsRefusal(response.StopReason?.ToString()))
        {
            var explanation = response.StopDetails is { } details ? details.Explanation : null;
            return AiCompletionResult.Fail(AiCompletionStatus.Refused, explanation ?? "The model declined to answer.", usage);
        }

        if (request.ResponseSchema is not null && !IsValidJson(text))
            return AiCompletionResult.Fail(AiCompletionStatus.Malformed, "The model response was not valid JSON for the requested schema.", usage);

        return AiCompletionResult.Ok(text, usage);
    }

    public async IAsyncEnumerable<AiCompletionChunk> StreamAsync(
        ResolvedAiProfile profile,
        AiCompletionRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var client = GetClient(profile.Provider);
        var parameters = BuildParameters(profile, request);

        long inputTokens = 0, outputTokens = 0, cacheRead = 0, cacheWrite = 0;
        string? stopReason = null;
        string? failure = null;
        var failureStatus = AiCompletionStatus.Unavailable;
        var collected = new StringBuilder();

        var enumerator = client.Messages.CreateStreaming(parameters, cancellationToken: ct)
            .GetAsyncEnumerator(ct);
        try
        {
            while (true)
            {
                bool moved;
                string? delta = null;
                try
                {
                    moved = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failureStatus = ClassifyException(ex);
                    failure = ex.Message;
                    break;
                }

                if (!moved)
                    break;

                var streamEvent = enumerator.Current;
                if (streamEvent.TryPickStart(out var start))
                {
                    inputTokens = start.Message.Usage.InputTokens;
                    cacheRead = start.Message.Usage.CacheReadInputTokens ?? 0;
                    cacheWrite = start.Message.Usage.CacheCreationInputTokens ?? 0;
                }
                else if (streamEvent.TryPickContentBlockDelta(out var blockDelta) &&
                         blockDelta.Delta.TryPickText(out var textDelta))
                {
                    delta = textDelta.Text;
                }
                else if (streamEvent.TryPickDelta(out var messageDelta))
                {
                    outputTokens = messageDelta.Usage.OutputTokens;
                    stopReason = messageDelta.Delta.StopReason?.ToString();
                }

                if (!string.IsNullOrEmpty(delta))
                {
                    collected.Append(delta);
                    yield return AiCompletionChunk.Delta(delta);
                }
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        var usage = new AiUsage(inputTokens, outputTokens, cacheRead, cacheWrite);
        if (failure is not null)
        {
            yield return AiCompletionChunk.Final(failureStatus, usage, failure);
        }
        else if (IsRefusal(stopReason))
        {
            yield return AiCompletionChunk.Final(AiCompletionStatus.Refused, usage, "The model declined to answer.");
        }
        else if (request.ResponseSchema is not null && !IsValidJson(collected.ToString()))
        {
            yield return AiCompletionChunk.Final(AiCompletionStatus.Malformed, usage, "The model response was not valid JSON for the requested schema.");
        }
        else
        {
            yield return AiCompletionChunk.Final(AiCompletionStatus.Ok, usage);
        }
    }

    private AnthropicClient GetClient(AiProviderOptions provider) =>
        _clients.GetOrAdd(provider.ApiKey, key => new AnthropicClient { ApiKey = key });

    internal static MessageCreateParams BuildParameters(ResolvedAiProfile profile, AiCompletionRequest request)
    {
        var messages = request.Messages
            .Select(m => new MessageParam
            {
                Role = string.Equals(m.Role, AiMessageRoles.Assistant, StringComparison.OrdinalIgnoreCase)
                    ? Role.Assistant
                    : Role.User,
                Content = m.Content
            })
            .ToList();

        var thinking = string.Equals(profile.Profile.Thinking, "adaptive", StringComparison.OrdinalIgnoreCase)
            ? new ThinkingConfigAdaptive()
            : (ThinkingConfigParam?)null;

        return new MessageCreateParams
        {
            Model = profile.Profile.Model,
            MaxTokens = profile.Profile.MaxTokens,
            Messages = messages,
            System = string.IsNullOrWhiteSpace(request.System) ? null : request.System,
            Thinking = thinking,
            OutputConfig = BuildOutputConfig(profile.Profile.Effort, request.ResponseSchema)
        };
    }

    internal static OutputConfig? BuildOutputConfig(string? effort, AiJsonSchema? schema)
    {
        Effort? mappedEffort = string.IsNullOrWhiteSpace(effort)
            ? null
            : effort.Trim().ToLowerInvariant() switch
            {
                "low" => Effort.Low,
                "medium" => Effort.Medium,
                "high" => Effort.High,
                "xhigh" => Effort.Xhigh,
                "max" => Effort.Max,
                _ => throw new InvalidOperationException($"Unsupported AI effort level '{effort}'. Use low, medium, high, xhigh, or max.")
            };

        JsonOutputFormat? format = null;
        if (schema is not null)
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(schema.SchemaJson)
                ?? throw new InvalidOperationException($"AiJsonSchema '{schema.Name}' does not contain a JSON object.");
            format = new JsonOutputFormat { Schema = parsed };
        }

        if (mappedEffort is null && format is null)
            return null;

        return new OutputConfig
        {
            Effort = mappedEffort,
            Format = format
        };
    }

    private static AiUsage MapUsage(Usage usage) => new(
        usage.InputTokens,
        usage.OutputTokens,
        usage.CacheReadInputTokens ?? 0,
        usage.CacheCreationInputTokens ?? 0);

    private static bool IsRefusal(string? stopReason) =>
        string.Equals(stopReason, "refusal", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        try
        {
            using var _ = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static AiCompletionStatus ClassifyException(Exception ex) => ex switch
    {
        AnthropicRateLimitException => AiCompletionStatus.Unavailable,
        Anthropic5xxException => AiCompletionStatus.Unavailable,
        AnthropicIOException => AiCompletionStatus.Unavailable,
        Anthropic4xxException => AiCompletionStatus.Malformed,
        _ => AiCompletionStatus.Unavailable
    };
}
