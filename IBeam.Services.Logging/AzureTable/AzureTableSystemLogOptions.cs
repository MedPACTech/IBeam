namespace IBeam.Services.Logging.AzureTable;

public sealed class AzureTableSystemLogOptions
{
    public const string SectionName = "IBeam:Logging:AzureTable";

    public string StorageConnectionString { get; set; } = string.Empty;

    public string TablePrefix { get; set; } = string.Empty;

    public string TableName { get; set; } = "SystemLogs";

    public bool CreateTableIfNotExists { get; set; } = true;

    public string FullTableName()
        => $"{TablePrefix}{TableName}";

    public void NormalizeAndValidate(bool requireConnectionString)
    {
        StorageConnectionString = (StorageConnectionString ?? string.Empty).Trim();
        TablePrefix = (TablePrefix ?? string.Empty).Trim();
        TableName = string.IsNullOrWhiteSpace(TableName) ? "SystemLogs" : TableName.Trim();

        if (requireConnectionString && string.IsNullOrWhiteSpace(StorageConnectionString))
        {
            throw new InvalidOperationException("Azure Table system logging connection string is required.");
        }

        ValidateTableName(TableName, nameof(TableName));
        ValidateTableName(FullTableName(), nameof(TablePrefix) + "+" + nameof(TableName));
    }

    private static void ValidateTableName(string name, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException($"{propertyName} is required.");
        }

        if (name.Length < 3 || name.Length > 63)
        {
            throw new InvalidOperationException($"{propertyName} must be 3-63 characters.");
        }

        for (var i = 0; i < name.Length; i++)
        {
            if (!char.IsLetterOrDigit(name[i]))
            {
                throw new InvalidOperationException($"{propertyName} must be alphanumeric only (Azure Tables rule).");
            }
        }
    }
}

