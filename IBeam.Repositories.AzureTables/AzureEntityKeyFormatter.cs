using Microsoft.Extensions.Options;

namespace IBeam.Repositories.AzureTables;

public sealed class AzureEntityKeyFormatter : IAzureEntityKeyFormatter
{
    public AzureEntityKeyFormatter(IOptions<AzureTablesOptions> options)
    {
        var value = options?.Value ?? throw new ArgumentNullException(nameof(options));
        GuidFormat = NormalizeGuidFormat(value.GuidKeyFormat);
        EnableLegacyFallbackReads = value.EnableLegacyGuidKeyFallbackReads;
    }

    public AzureEntityKeyFormatter(string guidFormat, bool enableLegacyFallbackReads)
    {
        GuidFormat = NormalizeGuidFormat(guidFormat);
        EnableLegacyFallbackReads = enableLegacyFallbackReads;
    }

    public string GuidFormat { get; }

    public bool EnableLegacyFallbackReads { get; }

    public string Format(Guid id)
        => id.ToString(GuidFormat);

    public IReadOnlyList<string> GetLookupCandidates(Guid id)
    {
        if (!EnableLegacyFallbackReads)
            return new[] { Format(id) };

        var primary = Format(id);
        var legacyFormat = string.Equals(GuidFormat, "N", StringComparison.OrdinalIgnoreCase) ? "D" : "N";
        var secondary = id.ToString(legacyFormat);

        return string.Equals(primary, secondary, StringComparison.Ordinal)
            ? new[] { primary }
            : new[] { primary, secondary };
    }

    internal static string NormalizeGuidFormat(string? format)
    {
        var normalized = string.IsNullOrWhiteSpace(format) ? "N" : format.Trim().ToUpperInvariant();
        if (normalized is not ("N" or "D"))
            throw new InvalidOperationException("AzureTablesOptions.GuidKeyFormat must be either 'N' or 'D'.");

        return normalized;
    }
}
