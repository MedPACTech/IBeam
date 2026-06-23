using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Services.ApiCredentials;

public sealed class ApiCredentialRoleCatalogProvider : IApiCredentialRoleCatalogProvider
{
    private static readonly ApiCredentialRoleCatalogEntry[] BuiltInEntries =
    [
        new(
            "API",
            "API",
            "Base API credential role for machine and agent callers.",
            "base",
            true,
            false,
            true),
        new(
            "tool:mcp",
            "MCP Tool Access",
            "Allows the credential to access MCP tool endpoints when the host app requires this scope.",
            "mcp",
            true,
            false,
            true),
        new(
            "api-scope:*",
            "API Scope Pattern",
            "Pattern for app-defined API or MCP module scopes such as api-scope:work.",
            "module",
            true,
            true,
            false),
        new(
            "api-scope:work",
            "Work",
            "Allows access to Work API and MCP tools.",
            "module",
            true,
            false,
            true),
        new(
            "api-scope:contacts",
            "Contacts",
            "Allows access to Contacts API and MCP tools.",
            "module",
            true,
            false,
            true),
        new(
            "api-scope:money",
            "Money",
            "Allows access to Money API and MCP tools.",
            "module",
            true,
            false,
            true),
        new(
            "agent:*",
            "Agent Pattern",
            "Pattern for agent-specific credential scopes such as agent:codex.",
            "agent",
            true,
            true,
            false)
    ];

    private readonly ApiCredentialOptions _options;

    public ApiCredentialRoleCatalogProvider(IOptions<ApiCredentialOptions> options)
    {
        _options = options.Value;
        _options.Validate();
    }

    public Task<IReadOnlyList<ApiCredentialRoleCatalogEntry>> ListAsync(CancellationToken ct = default)
    {
        var entries = BuiltInEntries
            .Concat(_options.RoleCatalog.Select(x => new ApiCredentialRoleCatalogEntry(
                x.Name,
                x.DisplayName ?? x.Name,
                x.Description ?? string.Empty,
                x.Category ?? "custom",
                false,
                x.IsPattern,
                x.IsAssignable)))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => CategoryOrder(x.Category))
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult<IReadOnlyList<ApiCredentialRoleCatalogEntry>>(entries);
    }

    private static int CategoryOrder(string category)
        => category.ToLowerInvariant() switch
        {
            "base" => 0,
            "mcp" => 1,
            "module" => 2,
            "agent" => 3,
            "permission" => 4,
            _ => 100
        };
}
