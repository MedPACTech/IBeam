namespace IBeam.Licensing;

public sealed class LicensingOptions
{
    public const string SectionName = "IBeam:Licensing";

    public List<LicensePlanOptions> Plans { get; set; } = [];

    public void Validate()
    {
        Plans = Plans
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .Select(x =>
            {
                x.Normalize();
                return x;
            })
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();
    }
}

public sealed class LicensePlanOptions
{
    public string Key { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public List<string> Entitlements { get; set; } = [];
    public Dictionary<string, int> Limits { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = [];

    internal void Normalize()
    {
        Key = Key.Trim();
        DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? Key : DisplayName.Trim();
        Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        Entitlements = Entitlements
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Limits = Limits
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .ToDictionary(x => x.Key.Trim(), x => x.Value, StringComparer.OrdinalIgnoreCase);
        Metadata = Metadata
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .ToDictionary(x => x.Key.Trim(), x => x.Value?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
    }
}
