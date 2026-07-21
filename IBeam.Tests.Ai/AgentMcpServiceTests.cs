using System.Security.Claims;
using System.Text.Json;
using IBeam.Ai;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace IBeam.Tests.Ai;

[TestClass]
public sealed class AgentMcpServiceTests
{
    [TestMethod]
    public async Task Initialize_ReturnsInformationalServerVersion()
    {
        var services = CreateServices(includeWorkScope: true);
        var sut = services.GetRequiredService<IAgentMcpService>();

        using var document = JsonDocument.Parse(
            """
            {
              "jsonrpc": "2.0",
              "id": 1,
              "method": "initialize",
              "params": {}
            }
            """);

        var result = await sut.HandleAsync(document.RootElement);
        var json = Serialize(result.Responses[0].Result);
        using var resultDocument = JsonDocument.Parse(json);
        var serverInfo = resultDocument.RootElement.GetProperty("serverInfo");
        var expectedVersion = typeof(AgentMcpService).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()
            ?.InformationalVersion;

        Assert.AreEqual("IBeam.Ai", serverInfo.GetProperty("name").GetString());
        Assert.AreEqual(expectedVersion, serverInfo.GetProperty("version").GetString());
    }

    [TestMethod]
    public async Task ToolsList_ReturnsOnlyToolsAllowedForCurrentAgent()
    {
        var services = CreateServices(includeWorkScope: true);
        var sut = services.GetRequiredService<IAgentMcpService>();

        using var document = JsonDocument.Parse(
            """
            {
              "jsonrpc": "2.0",
              "id": 1,
              "method": "tools/list",
              "params": {}
            }
            """);

        var result = await sut.HandleAsync(document.RootElement);
        var json = Serialize(result.Responses[0].Result);

        StringAssert.Contains(json, "demo.work.visible");
        Assert.IsFalse(json.Contains("demo.money.hidden", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ToolsCall_ReturnsForbidden_WhenAgentLacksRequiredScope()
    {
        var services = CreateServices(includeWorkScope: false);
        var sut = services.GetRequiredService<IAgentMcpService>();

        using var document = JsonDocument.Parse(
            """
            {
              "jsonrpc": "2.0",
              "id": 2,
              "method": "tools/call",
              "params": {
                "name": "demo.work.visible",
                "arguments": {}
              }
            }
            """);

        var result = await sut.HandleAsync(document.RootElement);

        Assert.AreEqual(McpErrorCodes.Forbidden, result.Responses[0].Error?.Code);
    }

    [TestMethod]
    public async Task ToolsCall_InvokesHandler_WithAgentContextAndArguments()
    {
        var services = CreateServices(includeWorkScope: true);
        var sut = services.GetRequiredService<IAgentMcpService>();

        using var document = JsonDocument.Parse(
            """
            {
              "jsonrpc": "2.0",
              "id": 3,
              "method": "tools/call",
              "params": {
                "name": "demo.work.visible",
                "arguments": {
                  "limit": 7
                }
              }
            }
            """);

        var result = await sut.HandleAsync(document.RootElement);
        var json = Serialize(result.Responses[0].Result);

        StringAssert.Contains(json, "codex");
        StringAssert.Contains(json, "7");
        StringAssert.Contains(json, "structuredContent");
    }

    private static ServiceProvider CreateServices(bool includeWorkScope)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIBeamAiMcp(tools =>
        {
            tools.AddTool(
                "demo.work.visible",
                "Visible work tool.",
                AgentToolSchemas.Object(
                    new Dictionary<string, object>
                    {
                        ["limit"] = AgentToolSchemas.Integer("Maximum items.")
                    },
                    []),
                (context, arguments, _) =>
                {
                    var limit = arguments.TryGetProperty("limit", out var value)
                        ? value.GetInt32()
                        : 0;

                    return ValueTask.FromResult<object?>(new
                    {
                        context.AgentKey,
                        limit
                    });
                },
                moduleKey: "work",
                requiredScopes: ["api-scope:work"]);

            tools.AddTool(
                "demo.money.hidden",
                "Hidden money tool.",
                AgentToolSchemas.EmptyObject(),
                (_, _, _) => ValueTask.FromResult<object?>(new { ok = true }),
                moduleKey: "money",
                requiredScopes: ["api-scope:money"]);
        });

        var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext
        {
            User = CreatePrincipal(includeWorkScope)
        };

        return provider;
    }

    private static ClaimsPrincipal CreatePrincipal(bool includeWorkScope)
    {
        var claims = new List<Claim>
        {
            new(AgentClaimTypes.AgentKey, "codex"),
            new(AgentClaimTypes.TenantId, Guid.NewGuid().ToString("D")),
            new(AgentClaimTypes.ApiCredentialId, Guid.NewGuid().ToString("D"))
        };

        if (includeWorkScope)
            claims.Add(new Claim("role", "api-scope:work"));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "IBeamApiKey"));
    }

    private static string Serialize(object? value)
        => JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
