namespace IBeam.Identity.Options;

public sealed class ApiCredentialOptions
{
    public const string SectionName = "IBeam:Identity:ApiCredentials";

    public string KeyPrefix { get; set; } = "ibk";
    public int SecretByteLength { get; set; } = 32;
    public int HashIterations { get; set; } = 100_000;
    public string ApiKeyHeaderName { get; set; } = "X-API-Key";
    public string AuthorizationSchemeName { get; set; } = "ApiKey";
    public List<ApiCredentialRoleCatalogEntryOptions> RoleCatalog { get; set; } = [];
    public List<ApiScopeCatalogEntryOptions> ScopeCatalog { get; set; } = [];
    public List<string> DeniedCredentialRoleNames { get; set; } =
    [
        "Owner",
        "Administrator",
        "Admin"
    ];

    public void Validate()
    {
        KeyPrefix = Normalize(KeyPrefix, "ibk");
        if (KeyPrefix.Length < 2 || KeyPrefix.Length > 12)
            throw new InvalidOperationException($"{SectionName}:{nameof(KeyPrefix)} must be 2-12 characters.");
        if (KeyPrefix.Any(c => !char.IsLetterOrDigit(c)))
            throw new InvalidOperationException($"{SectionName}:{nameof(KeyPrefix)} must be alphanumeric.");

        if (SecretByteLength < 24)
            throw new InvalidOperationException($"{SectionName}:{nameof(SecretByteLength)} must be at least 24.");
        if (HashIterations < 50_000)
            throw new InvalidOperationException($"{SectionName}:{nameof(HashIterations)} must be at least 50000.");

        ApiKeyHeaderName = Normalize(ApiKeyHeaderName, "X-API-Key");
        AuthorizationSchemeName = Normalize(AuthorizationSchemeName, "ApiKey");
        DeniedCredentialRoleNames = DeniedCredentialRoleNames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        RoleCatalog = RoleCatalog
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .Select(x =>
            {
                x.Normalize();
                return x;
            })
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();

        ScopeCatalog = ScopeCatalog
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .Select(x =>
            {
                x.Normalize();
                return x;
            })
            .GroupBy(x => $"{x.Category}|{x.Key}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();
    }

    private static string Normalize(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}

public sealed class ApiScopeCatalogEntryOptions
{
    public string Key { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public bool IsAssignable { get; set; } = true;
    public bool IsWildcardCapable { get; set; }
    public string? RequiredParentScope { get; set; }
    public string? ModuleKey { get; set; }
    public string? ResourceType { get; set; }

    internal void Normalize()
    {
        Key = Key.Trim();
        DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? Key : DisplayName.Trim();
        Description = string.IsNullOrWhiteSpace(Description) ? string.Empty : Description.Trim();
        Category = string.IsNullOrWhiteSpace(Category) ? "module" : Category.Trim();
        RequiredParentScope = string.IsNullOrWhiteSpace(RequiredParentScope) ? null : RequiredParentScope.Trim();
        ModuleKey = string.IsNullOrWhiteSpace(ModuleKey) ? null : ModuleKey.Trim();
        ResourceType = string.IsNullOrWhiteSpace(ResourceType) ? null : ResourceType.Trim();
    }
}

public sealed class ApiCredentialOptionsBuilder
{
    private readonly ApiCredentialOptions _options;

    public ApiCredentialOptionsBuilder(ApiCredentialOptions options)
    {
        _options = options;
        Scopes = new ApiCredentialScopeCatalogBuilder(options.ScopeCatalog);
    }

    public string KeyPrefix
    {
        get => _options.KeyPrefix;
        set => _options.KeyPrefix = value;
    }

    public ApiCredentialScopeCatalogBuilder Scopes { get; }
}

public sealed class ApiCredentialScopeCatalogBuilder
{
    private readonly List<ApiScopeCatalogEntryOptions> _entries;

    public ApiCredentialScopeCatalogBuilder(List<ApiScopeCatalogEntryOptions> entries)
    {
        _entries = entries;
    }

    public ApiCredentialScopeCatalogBuilder AddModule(
        string key,
        string displayName,
        string description,
        bool isWildcardCapable = true)
    {
        _entries.Add(new ApiScopeCatalogEntryOptions
        {
            Key = key,
            DisplayName = displayName,
            Description = description,
            Category = "module",
            IsAssignable = true,
            IsWildcardCapable = isWildcardCapable,
            ModuleKey = key
        });
        return this;
    }

    public ApiCredentialScopeCatalogBuilder AddTool(
        string key,
        string displayName,
        string description,
        bool isWildcardCapable = false)
    {
        _entries.Add(new ApiScopeCatalogEntryOptions
        {
            Key = key,
            DisplayName = displayName,
            Description = description,
            Category = "tool",
            IsAssignable = true,
            IsWildcardCapable = isWildcardCapable
        });
        return this;
    }
}

public sealed class ApiCredentialRoleCatalogEntryOptions
{
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public bool IsPattern { get; set; }
    public bool IsAssignable { get; set; } = true;

    internal void Normalize()
    {
        Name = Name.Trim();
        DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName.Trim();
        Description = string.IsNullOrWhiteSpace(Description) ? string.Empty : Description.Trim();
        Category = string.IsNullOrWhiteSpace(Category) ? "custom" : Category.Trim();
    }
}
