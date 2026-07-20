namespace IBeam.AccessControl.Repositories.AzureTable;

public sealed class AzureTableAccessControlOptions
{
    public const string SectionName = "IBeam:AccessControl:AzureTable";

    public string StorageConnectionString { get; set; } = string.Empty;

    public string TablePrefix { get; set; } = string.Empty;

    public string ResourceAccessGrantsTableName { get; set; } = "AccessGrants";

    public string PermissionRoleMapsTableName { get; set; } = "PermissionRoleMaps";

    public string ServiceOperationPermissionsTableName { get; set; } = "ServiceOperationPermissions";

    public bool CreateTablesIfNotExists { get; set; } = true;

    public string FullTableName(string tableName)
        => $"{TablePrefix}{tableName}";

    public void Validate()
    {
        StorageConnectionString = (StorageConnectionString ?? string.Empty).Trim();
        TablePrefix = (TablePrefix ?? string.Empty).Trim();
        ResourceAccessGrantsTableName = NormalizeOrDefault(ResourceAccessGrantsTableName, "AccessGrants");
        PermissionRoleMapsTableName = NormalizeOrDefault(PermissionRoleMapsTableName, "PermissionRoleMaps");
        ServiceOperationPermissionsTableName = string.IsNullOrWhiteSpace(ServiceOperationPermissionsTableName)
            ? "ServiceOperationPermissions"
            : ServiceOperationPermissionsTableName.Trim();

        if (string.IsNullOrWhiteSpace(StorageConnectionString))
        {
            throw new InvalidOperationException("AzureTableAccessControlOptions.StorageConnectionString is required.");
        }

        ValidateTableName(ResourceAccessGrantsTableName, nameof(ResourceAccessGrantsTableName));
        ValidateTableName(PermissionRoleMapsTableName, nameof(PermissionRoleMapsTableName));
        ValidateTableName(ServiceOperationPermissionsTableName, nameof(ServiceOperationPermissionsTableName));
        ValidateTableName(FullTableName(ResourceAccessGrantsTableName), nameof(TablePrefix) + "+" + nameof(ResourceAccessGrantsTableName));
        ValidateTableName(FullTableName(PermissionRoleMapsTableName), nameof(TablePrefix) + "+" + nameof(PermissionRoleMapsTableName));
        ValidateTableName(FullTableName(ServiceOperationPermissionsTableName), nameof(TablePrefix) + "+" + nameof(ServiceOperationPermissionsTableName));
    }

    public string ResourceAccessGrantsPk(Guid tenantId)
        => $"TEN|{tenantId:D}";

    public string ResourceAccessGrantsRk(Guid grantId)
        => $"AGR|{grantId:D}";

    public string PermissionRoleMapsPk(Guid tenantId)
        => $"TEN|{tenantId:D}";

    public string PermissionRoleMapByNameRk(string normalizedPermissionName)
        => $"NAM|{normalizedPermissionName}";

    public string PermissionRoleMapByIdRk(Guid permissionId)
        => $"ID|{permissionId:D}";

    public string ServiceOperationPermissionsPk(Guid tenantId)
        => $"TEN|{tenantId:D}";

    public string ServiceOperationPermissionsRk(Guid ruleId)
        => $"SOP|{ruleId:D}";

    private static string NormalizeOrDefault(string? value, string defaultValue)
        => string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();

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

