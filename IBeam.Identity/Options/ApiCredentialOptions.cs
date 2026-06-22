namespace IBeam.Identity.Options;

public sealed class ApiCredentialOptions
{
    public const string SectionName = "IBeam:Identity:ApiCredentials";

    public string KeyPrefix { get; set; } = "ibk";
    public int SecretByteLength { get; set; } = 32;
    public int HashIterations { get; set; } = 100_000;
    public string ApiKeyHeaderName { get; set; } = "X-API-Key";
    public string AuthorizationSchemeName { get; set; } = "ApiKey";
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
    }

    private static string Normalize(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
