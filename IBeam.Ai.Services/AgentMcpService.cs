using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace IBeam.Ai;

public sealed class AgentMcpService : IAgentMcpService
{
    public const string ProtocolVersion = "2025-06-18";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IAgentToolCatalog _catalog;
    private readonly IAgentToolAccessPolicy _accessPolicy;
    private readonly IAgentToolContextFactory _contextFactory;
    private readonly IServiceProvider _services;
    private readonly ILogger<AgentMcpService> _logger;

    public AgentMcpService(
        IAgentToolCatalog catalog,
        IAgentToolAccessPolicy accessPolicy,
        IAgentToolContextFactory contextFactory,
        IServiceProvider services,
        ILogger<AgentMcpService> logger)
    {
        _catalog = catalog;
        _accessPolicy = accessPolicy;
        _contextFactory = contextFactory;
        _services = services;
        _logger = logger;
    }

    public async Task<McpHttpResult> HandleAsync(JsonElement payload, CancellationToken cancellationToken = default)
    {
        if (payload.ValueKind == JsonValueKind.Array)
            return await HandleBatchAsync(payload, cancellationToken).ConfigureAwait(false);

        var single = await HandleSingleAsync(payload, cancellationToken).ConfigureAwait(false);
        return single is null
            ? new McpHttpResult()
            : new McpHttpResult { Responses = [single] };
    }

    private async Task<McpHttpResult> HandleBatchAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        if (payload.GetArrayLength() == 0)
        {
            return new McpHttpResult
            {
                IsBatch = true,
                Responses = [Error(null, McpErrorCodes.InvalidRequest, "JSON-RPC batch requests cannot be empty.")]
            };
        }

        var responses = new List<McpJsonRpcResponse>();
        foreach (var item in payload.EnumerateArray())
        {
            var response = await HandleSingleAsync(item, cancellationToken).ConfigureAwait(false);
            if (response is not null)
                responses.Add(response);
        }

        return new McpHttpResult { IsBatch = true, Responses = responses };
    }

    private async Task<McpJsonRpcResponse?> HandleSingleAsync(JsonElement request, CancellationToken cancellationToken)
    {
        if (request.ValueKind != JsonValueKind.Object)
            return Error(null, McpErrorCodes.InvalidRequest, "Each JSON-RPC request must be an object.");

        var id = TryCloneProperty(request, "id");
        if (!TryGetStringProperty(request, "jsonrpc", out var jsonRpc) ||
            !string.Equals(jsonRpc, "2.0", StringComparison.Ordinal))
        {
            return Error(id, McpErrorCodes.InvalidRequest, "JSON-RPC version must be 2.0.");
        }

        if (!TryGetStringProperty(request, "method", out var method))
            return Error(id, McpErrorCodes.InvalidRequest, "JSON-RPC method is required.");

        if (id is null)
        {
            if (!string.Equals(method, "notifications/initialized", StringComparison.Ordinal))
                _logger.LogWarning("Ignoring MCP notification method {Method} because IBeam tools require request ids.", method);

            return null;
        }

        try
        {
            return method switch
            {
                "initialize" => Success(id, BuildInitializeResult()),
                "ping" => Success(id, new { }),
                "tools/list" => Success(id, await ListToolsAsync(cancellationToken).ConfigureAwait(false)),
                "tools/call" => await CallToolAsync(id, GetParams(request), cancellationToken).ConfigureAwait(false),
                "notifications/initialized" => Success(id, new { }),
                _ => Error(id, McpErrorCodes.MethodNotFound, $"Unsupported MCP method '{method}'.")
            };
        }
        catch (ArgumentException ex)
        {
            return Error(id, McpErrorCodes.InvalidParams, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled MCP request failure for method {Method}.", method);
            return Error(id, McpErrorCodes.InternalError, "The MCP request failed.");
        }
    }

    private static object BuildInitializeResult()
        => new
        {
            protocolVersion = ProtocolVersion,
            capabilities = new
            {
                tools = new
                {
                    listChanged = false
                }
            },
            serverInfo = new
            {
                name = "IBeam.Ai",
                version = typeof(AgentMcpService).Assembly.GetName().Version?.ToString() ?? "1.0.0"
            }
        };

    private async Task<object> ListToolsAsync(CancellationToken cancellationToken)
    {
        var context = _contextFactory.Create(_services);
        var visibleTools = new List<McpToolDefinition>();

        foreach (var tool in _catalog.Tools)
        {
            var access = await _accessPolicy.CanAccessAsync(context, tool, cancellationToken).ConfigureAwait(false);
            if (access.Allowed)
            {
                visibleTools.Add(new McpToolDefinition(
                    tool.Name,
                    tool.Description,
                    tool.InputSchema));
            }
        }

        return new { tools = visibleTools };
    }

    private async Task<McpJsonRpcResponse> CallToolAsync(
        JsonElement? id,
        JsonElement? parameters,
        CancellationToken cancellationToken)
    {
        if (parameters is null || parameters.Value.ValueKind != JsonValueKind.Object)
            return Error(id, McpErrorCodes.InvalidParams, "tools/call params must be an object.");

        if (!TryGetStringProperty(parameters.Value, "name", out var name))
            return Error(id, McpErrorCodes.InvalidParams, "tools/call requires a tool name.");

        var tool = _catalog.Find(name);
        if (tool is null)
            return Error(id, McpErrorCodes.InvalidParams, $"Unknown MCP tool '{name}'.");

        var context = _contextFactory.Create(_services);
        var access = await _accessPolicy.CanAccessAsync(context, tool, cancellationToken).ConfigureAwait(false);
        if (!access.Allowed)
            return Error(id, McpErrorCodes.Forbidden, access.Reason ?? "The current agent is not allowed to call this tool.");

        var arguments = GetArguments(parameters.Value);
        var result = await tool.Handler(context, arguments, cancellationToken).ConfigureAwait(false);
        return Success(id, ToolResult(result));
    }

    private static object ToolResult(object? data)
    {
        var text = JsonSerializer.Serialize(data, JsonOptions);
        return new McpToolCallResult(
            [new McpToolContent("text", text)],
            data);
    }

    private static JsonElement? TryCloneProperty(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) ? value.Clone() : null;

    private static JsonElement? GetParams(JsonElement request)
        => request.TryGetProperty("params", out var value) ? value : null;

    private static JsonElement GetArguments(JsonElement parameters)
        => parameters.TryGetProperty("arguments", out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : EmptyArguments();

    private static JsonElement EmptyArguments()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private static bool TryGetStringProperty(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            return false;

        value = property.GetString() ?? string.Empty;
        return true;
    }

    private static McpJsonRpcResponse Success(JsonElement? id, object result)
        => new()
        {
            Id = id,
            Result = result
        };

    private static McpJsonRpcResponse Error(JsonElement? id, int code, string message, object? data = null)
        => new()
        {
            Id = id,
            Error = new McpJsonRpcError
            {
                Code = code,
                Message = message,
                Data = data
            }
        };
}
