using System;
using System.ComponentModel.DataAnnotations;

namespace IBeam.Identity.Repositories.AzureTable.Options;

public sealed class AzureTableIdentityOptions
{
    // Connection
    [Required]
    public string StorageConnectionString { get; set; } = string.Empty;

    // Prefix applied to ALL tables (ElCamino + custom)
    public string TablePrefix { get; set; } = string.Empty;

    // --- ElCamino identity tables (match IdentityConfiguration) ---
    public string IndexTableName { get; set; } = "AspNetIndex";
    public string UserTableName { get; set; } = "AspNetUsers";
    public string RoleTableName { get; set; } = "AspNetRoles";

    // --- Custom provider tables ---
    public string TenantsTableName { get; set; } = "Tenants";
    public string TenantUsersTableName { get; set; } = "TenantUsers";
    public string UserTenantsTableName { get; set; } = "UserTenants";
    public string OtpChallengesTableName { get; set; } = "OtpChallenges";

    // (Optional, only if you implement later)
    public string? OtpAttemptsTableName { get; set; } = null;

    // ----- Table name helper -----
    public string FullTableName(string baseName)
        => $"{TablePrefix}{baseName}";

    // ----- Key helpers (membership tables) -----
    // UserTenants: PK = "USR#{userId}", RK = "TEN#{tenantId}"
    public string UserTenantsPk(string userId) => $"USR#{NormalizeId(userId)}";
    public string UserTenantsRk(Guid tenantId) => $"TEN#{tenantId:D}";

    public bool TryParseTenantIdFromUserTenantsRk(string rowKey, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(rowKey)) return false;
        if (!rowKey.StartsWith("TEN#", StringComparison.OrdinalIgnoreCase)) return false;

        return Guid.TryParse(rowKey.Substring(4), out tenantId);
    }

    // TenantUsers: PK = "TEN#{tenantId}", RK = "USR#{userId}"
    public string TenantUsersPk(Guid tenantId) => $"TEN#{tenantId:D}";
    public string TenantUsersRk(string userId) => $"USR#{NormalizeId(userId)}";

    public bool TryParseUserIdFromTenantUsersRk(string rowKey, out string userId)
    {
        userId = string.Empty;
        if (string.IsNullOrWhiteSpace(rowKey)) return false;
        if (!rowKey.StartsWith("USR#", StringComparison.OrdinalIgnoreCase)) return false;

        userId = rowKey.Substring(4);
        return !string.IsNullOrWhiteSpace(userId);
    }

    // Tenants: PK = "TEN", RK = tenantId
    public const string TenantsPk = "TEN";
    public string TenantsRk(Guid tenantId) => tenantId.ToString("D");

    // ----- Validation -----
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(StorageConnectionString))
            throw new InvalidOperationException("AzureTableIdentityOptions.StorageConnectionString is required.");

        TablePrefix = (TablePrefix ?? string.Empty).Trim();

        // normalize empties to defaults
        IndexTableName = NormalizeOrDefault(IndexTableName, "AspNetIndex");
        UserTableName = NormalizeOrDefault(UserTableName, "AspNetUsers");
        RoleTableName = NormalizeOrDefault(RoleTableName, "AspNetRoles");

        TenantsTableName = NormalizeOrDefault(TenantsTableName, "Tenants");
        TenantUsersTableName = NormalizeOrDefault(TenantUsersTableName, "TenantUsers");
        UserTenantsTableName = NormalizeOrDefault(UserTenantsTableName, "UserTenants");
        OtpChallengesTableName = NormalizeOrDefault(OtpChallengesTableName, "OtpChallenges");

        // Validate base table names (prefix is not validated here; it becomes part of final name)
        ValidateTableName(IndexTableName, nameof(IndexTableName));
        ValidateTableName(UserTableName, nameof(UserTableName));
        ValidateTableName(RoleTableName, nameof(RoleTableName));

        ValidateTableName(TenantsTableName, nameof(TenantsTableName));
        ValidateTableName(TenantUsersTableName, nameof(TenantUsersTableName));
        ValidateTableName(UserTenantsTableName, nameof(UserTenantsTableName));
        ValidateTableName(OtpChallengesTableName, nameof(OtpChallengesTableName));

        if (!string.IsNullOrWhiteSpace(OtpAttemptsTableName))
            ValidateTableName(OtpAttemptsTableName!, nameof(OtpAttemptsTableName));

        // Validate full table names too (prefix+name must still be valid)
        ValidateTableName(FullTableName(IndexTableName), nameof(TablePrefix) + "+" + nameof(IndexTableName));
        ValidateTableName(FullTableName(UserTableName), nameof(TablePrefix) + "+" + nameof(UserTableName));
        ValidateTableName(FullTableName(RoleTableName), nameof(TablePrefix) + "+" + nameof(RoleTableName));

        ValidateTableName(FullTableName(TenantsTableName), nameof(TablePrefix) + "+" + nameof(TenantsTableName));
        ValidateTableName(FullTableName(TenantUsersTableName), nameof(TablePrefix) + "+" + nameof(TenantUsersTableName));
        ValidateTableName(FullTableName(UserTenantsTableName), nameof(TablePrefix) + "+" + nameof(UserTenantsTableName));
        ValidateTableName(FullTableName(OtpChallengesTableName), nameof(TablePrefix) + "+" + nameof(OtpChallengesTableName));
    }

    private static string NormalizeOrDefault(string value, string @default)
        => string.IsNullOrWhiteSpace(value) ? @default : value.Trim();

    private static string NormalizeId(string id)
        => (id ?? string.Empty).Trim();

    private static void ValidateTableName(string name, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException($"{propertyName} is required.");

        if (name.Length < 3 || name.Length > 63)
            throw new InvalidOperationException($"{propertyName} must be 3-63 characters.");

        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (!char.IsLetterOrDigit(c))
                throw new InvalidOperationException($"{propertyName} must be alphanumeric only (Azure Tables rule).");
        }
    }
}
