namespace IBeam.Repositories.AzureTables;

public sealed class AzureTablesOptions
{
    public const string SectionName = "IBeam:Repositories:AzureTables";

    public string ConnectionString { get; set; } = string.Empty;
    public string? TableNamePrefix { get; set; }
    public bool CreateTablesIfNotExists { get; set; } = true;
    public AzureTableStorageModel StorageModel { get; set; } = AzureTableStorageModel.Envelope;
    public string GuidKeyFormat { get; set; } = "N";
    public bool EnableLegacyGuidKeyFallbackReads { get; set; } = false;
}

public enum AzureTableStorageModel
{
    Envelope = 0,
    EntityColumns = 1
}
