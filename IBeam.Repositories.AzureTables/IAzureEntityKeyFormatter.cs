namespace IBeam.Repositories.AzureTables;

public interface IAzureEntityKeyFormatter
{
    string GuidFormat { get; }

    bool EnableLegacyFallbackReads { get; }

    string Format(Guid id);

    IReadOnlyList<string> GetLookupCandidates(Guid id);
}
