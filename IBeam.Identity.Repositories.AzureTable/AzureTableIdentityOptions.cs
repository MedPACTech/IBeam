
namespace IBeam.Identity.Repositories.AzureTable;

public sealed class AzureTableIdentityOptions
{
    public string StorageConnectionString { get; init; } = string.Empty;
    public string TablePrefix { get; init; } = "";
    public string? UserTableName { get; init; }
    public string? RoleTableName { get; init; }
    public string? IndexTableName { get; init; }
    public string KeyDelimiter { get; init; } = "|";
    public string UserKeyPrefix { get; init; } = "USR";
    public string TenantKeyPrefix { get; init; } = "TEN";

    public string UserPk(string userId) => $"{UserKeyPrefix}{KeyDelimiter}{userId}";
    public string UserRk(string userId) => $"{UserKeyPrefix}{KeyDelimiter}{userId}";

    public string TenantPk(Guid tenantId) => $"{TenantKeyPrefix}{KeyDelimiter}{tenantId:D}";
    public string TenantRk(Guid tenantId) => $"{TenantKeyPrefix}{KeyDelimiter}{tenantId:D}";

    public bool TryParseTenantIdFromRowKey(string rowKey, out Guid tenantId)
    {
        tenantId = default;
        var prefix = $"{TenantKeyPrefix}{KeyDelimiter}";
        if (!rowKey.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        return Guid.TryParse(rowKey.Substring(prefix.Length), out tenantId);
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(StorageConnectionString))
            throw new InvalidOperationException("IdentityAzureTable:IdentityConfiguration:StorageConnectionString is required.");

        // Table names: letters/digits only, start with letter, 3-63 chars.
        // Prefix can be empty, but if present must be letters/digits only.
        if (!string.IsNullOrEmpty(TablePrefix) && !TablePrefix.All(char.IsLetterOrDigit))
            throw new InvalidOperationException("TablePrefix must be alphanumeric (letters/digits only).");


        if (string.IsNullOrWhiteSpace(KeyDelimiter))
            throw new InvalidOperationException("KeyDelimiter is required.");

        if (KeyDelimiter.Any(c => InvalidKeyChars.Contains(c)))
            throw new InvalidOperationException("KeyDelimiter contains invalid Azure Table key characters.");

        if (string.IsNullOrWhiteSpace(UserKeyPrefix))
            throw new InvalidOperationException("UserKeyPrefix is required.");

        if (string.IsNullOrWhiteSpace(TenantKeyPrefix))
            throw new InvalidOperationException("TenantKeyPrefix is required.");

        if (KeyDelimiter.Length > 3)
            throw new InvalidOperationException("KeyDelimiter is too long (keep it short).");

    }

    private static readonly char[] InvalidKeyChars = new[] { '/', '\\', '#', '?', '\t', '\n', '\r' };
}

