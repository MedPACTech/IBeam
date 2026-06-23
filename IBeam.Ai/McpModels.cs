using System.Text.Json;
using System.Text.Json.Serialization;

namespace IBeam.Ai;

public sealed class McpHttpResult
{
    public bool IsBatch { get; init; }
    public IReadOnlyList<McpJsonRpcResponse> Responses { get; init; } = [];
    public bool HasResponse => Responses.Count > 0;
}

public sealed class McpJsonRpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; init; } = "2.0";

    [JsonPropertyName("id")]
    public JsonElement? Id { get; init; }

    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Result { get; init; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public McpJsonRpcError? Error { get; init; }
}

public sealed class McpJsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Data { get; init; }
}

public sealed record McpToolDefinition(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("inputSchema")] object InputSchema);

public sealed record McpToolContent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string Text);

public sealed record McpToolCallResult(
    [property: JsonPropertyName("content")] IReadOnlyList<McpToolContent> Content,
    [property: JsonPropertyName("structuredContent")] object? StructuredContent);
