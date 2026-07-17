namespace IBeam.AccessControl.Repositories.AzureTable;

public sealed class AzureTableAccessControlOptions
{
    public const string SectionName = "IBeam:AccessControl:AzureTable";

    public string StorageConnectionString { get; set; } = string.Empty;

    public string TablePrefix { get; set; } = string.Empty;

    public string ServiceOperationPermissionsTableName { get; set; } = "ServiceOperationPermissions";

    public bool CreateTablesIfNotExists { get; set; } = true;

    public string FullTableName(string tableName)
        => $"{TablePrefix}{tableName}";

    public void Validate()
    {
        StorageConnectionString = (StorageConnectionString ?? string.Empty).Trim();
        TablePrefix = (TablePrefix ?? string.Empty).Trim();
        ServiceOperationPermissionsTableName = string.IsNullOrWhiteSpace(ServiceOperationPermissionsTableName)
            ? "ServiceOperationPermissions"
            : ServiceOperationPermissionsTableName.Trim();

        if (string.IsNullOrWhiteSpace(StorageConnectionString))
        {
            throw new InvalidOperationException("AzureTableAccessControlOptions.StorageConnectionString is required.");
        }

        ValidateTableName(ServiceOperationPermissionsTableName, nameof(ServiceOperationPermissionsTableName));
        ValidateTableName(FullTableName(ServiceOperationPermissionsTableName), nameof(TablePrefix) + "+" + nameof(ServiceOperationPermissionsTableName));
    }

    public string ServiceOperationPermissionsPk(Guid tenantId)
        => $"TEN|{tenantId:D}";

    public string ServiceOperationPermissionsRk(Guid ruleId)
        => $"SOP|{ruleId:D}";

    private static void ValidateTableName(string name, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException($"{propertyName} is required.");

        if (name.Length < 3 || name.Length > 63)
            throw new InvalidOperationException($"{propertyName} must be 3-63 characters.");

        for (var i = 0; i < name.Length; i++)
        {
            if (!char.IsLetterOrDigit(name[i]))
                throw new InvalidOperationException($"{propertyName} must be alphanumeric only (Azure Tables rule).");
        }
    }
}

