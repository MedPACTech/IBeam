using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Services.ApiCredentials;

public sealed class ApiCredentialScopeCatalogProvider : IApiCredentialScopeCatalogProvider, IIBeamApiScopeCatalogProvider
{
    private static readonly ApiScopeCatalogItem[] BuiltInScopes =
    [
        new("work", "Work API", "Allows access to Work API features.", "module", true, true, ModuleKey: "work"),
        new("planning", "Planning API", "Allows access to Planning API features.", "module", true, true, ModuleKey: "planning"),
        new("products", "Products API", "Allows access to Products and Projects API features.", "module", true, true, ModuleKey: "products"),
        new("ops", "Operations API", "Allows access to Operations API features.", "module", true, true, ModuleKey: "ops"),
        new("content", "Content API", "Allows access to Content API features.", "module", true, true, ModuleKey: "content"),
        new("money", "Money API", "Allows access to Money API features.", "module", true, true, ModuleKey: "money"),
        new("contacts", "Contacts API", "Allows access to Contacts API features.", "module", true, true, ModuleKey: "contacts"),
        new("email", "Email API", "Allows email API access.", "module", true, true, ModuleKey: "email"),
        new("mcp", "MCP Tools", "Allows access to MCP tool endpoints.", "tool", true, false)
    ];

    private readonly ApiCredentialOptions _options;

    public ApiCredentialScopeCatalogProvider(IOptions<ApiCredentialOptions> options)
    {
        _options = options.Value;
        _options.Validate();
    }

    public Task<IReadOnlyList<ApiScopeCatalogItem>> GetScopesAsync(Guid tenantId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var configured = _options.ScopeCatalog.Select(x => new ApiScopeCatalogItem(
            x.Key,
            x.DisplayName ?? x.Key,
            x.Description ?? string.Empty,
            x.Category ?? "module",
            x.IsAssignable,
            x.IsWildcardCapable,
            x.RequiredParentScope,
            x.ModuleKey,
            x.ResourceType));

        var result = BuiltInScopes
            .Concat(configured)
            .GroupBy(x => $"{x.Category}|{x.Key}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => CategoryOrder(x.Category))
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult<IReadOnlyList<ApiScopeCatalogItem>>(result);
    }

    private static int CategoryOrder(string category)
        => category.ToLowerInvariant() switch
        {
            "module" => 0,
            "tool" => 1,
            "agent" => 2,
            "resource" => 3,
            _ => 100
        };
}
