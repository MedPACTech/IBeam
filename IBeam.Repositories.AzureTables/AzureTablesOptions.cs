namespace IBeam.Repositories.AzureTables;

public sealed class AzureTablesOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string? TableNamePrefix { get; set; }
    public bool CreateTablesIfNotExists { get; set; } = true;
}
