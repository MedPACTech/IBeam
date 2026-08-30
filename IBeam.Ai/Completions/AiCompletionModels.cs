namespace IBeam.Ai.Completions;

/// <summary>
/// Terminal state of a completion call. Modelled failure, not exceptions: a refusal,
/// a missing API key, and a rate limit are different things a caller must be able to
/// tell apart and say different words about.
/// </summary>
public enum AiCompletionStatus
{
    Ok,
    NotConfigured,
    Refused,
    Unavailable,
    Malformed,
    Filtered,
    Denied
}

public static class AiMessageRoles
{
    public const string User = "user";
    public const string Assistant = "assistant";
}

public sealed class AiMessage
{
    public string Role { get; set; } = AiMessageRoles.User;
    public string Content { get; set; } = string.Empty;

    public static AiMessage User(string content) => new() { Role = AiMessageRoles.User, Content = content };
    public static AiMessage Assistant(string content) => new() { Role = AiMessageRoles.Assistant, Content = content };
}

/// <summary>
/// Structured-output contract: a raw JSON Schema document the provider constrains
/// the response to. Kept as a string so the contracts stay vendor- and serializer-free.
/// </summary>
public sealed class AiJsonSchema
{
    public string Name { get; set; } = string.Empty;
    public string SchemaJson { get; set; } = string.Empty;
}

/// <summary>
/// Credit-metering context for a call against a metered profile. The consuming app
/// supplies who is spending; the profile supplies which bucket and at what rate.
/// </summary>
public sealed class AiCreditContext
{
    public Guid TenantId { get; set; }
    public string SubjectType { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public string Entitlement { get; set; } = string.Empty;
    public Guid CreditAccountId { get; set; }
}

public sealed class AiCompletionRequest
{
    /// <summary>Named profile from configuration. Empty uses AiOptions.DefaultProfile.</summary>
    public string Profile { get; set; } = string.Empty;

    public string? System { get; set; }
    public IReadOnlyList<AiMessage> Messages { get; set; } = [];
    public AiJsonSchema? ResponseSchema { get; set; }
    public AiCreditContext? Credit { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
}

public sealed record AiUsage(
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens)
{
    public static readonly AiUsage Empty = new(0, 0, 0, 0);

    public AiUsage Add(AiUsage other) => new(
        InputTokens + other.InputTokens,
        OutputTokens + other.OutputTokens,
        CacheReadTokens + other.CacheReadTokens,
        CacheWriteTokens + other.CacheWriteTokens);
}

public sealed record AiCompletionResult(
    bool Succeeded,
    string Text,
    AiCompletionStatus Status,
    AiUsage Usage,
    string? ErrorMessage)
{
    public static AiCompletionResult Ok(string text, AiUsage usage) =>
        new(true, text, AiCompletionStatus.Ok, usage, null);

    public static AiCompletionResult Fail(AiCompletionStatus status, string? errorMessage, AiUsage? usage = null) =>
        new(false, string.Empty, status, usage ?? AiUsage.Empty, errorMessage);
}

/// <summary>
/// One streamed increment. Text arrives on non-final chunks; the final chunk carries
/// the terminal status and the total usage for the call.
/// </summary>
public sealed record AiCompletionChunk(
    string TextDelta,
    bool IsFinal,
    AiCompletionStatus? FinalStatus = null,
    AiUsage? FinalUsage = null,
    string? ErrorMessage = null)
{
    public static AiCompletionChunk Delta(string text) => new(text, false);

    public static AiCompletionChunk Final(AiCompletionStatus status, AiUsage usage, string? errorMessage = null) =>
        new(string.Empty, true, status, usage, errorMessage);
}
