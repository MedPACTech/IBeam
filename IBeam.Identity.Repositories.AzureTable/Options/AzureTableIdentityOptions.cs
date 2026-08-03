using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;

namespace IBeam.Identity.Repositories.AzureTable.Options;

public sealed class AzureTableIdentityOptions
{
    public const string SectionName = "IBeam:Identity:AzureTable";
    // Connection
    [Required]
    public string StorageConnectionString { get; set; } = string.Empty;

    // Prefix applied to ALL tables (ElCamino + custom)
    public string TablePrefix { get; set; } = string.Empty;

    // Schema/bootstrap behavior
    public bool CreateTablesIfNotExists { get; set; } = true;

    // --- ElCamino identity tables (match IdentityConfiguration) ---
    public string IndexTableName { get; set; } = "AspNetIndex";
    public string UserTableName { get; set; } = "AspNetUsers";
    public string RoleTableName { get; set; } = "AspNetRoles";

    // --- Custom provider tables ---
    public string TenantsTableName { get; set; } = "Tenants";
    public string TenantUsersTableName { get; set; } = "TenantUsers";
    public string UserTenantsTableName { get; set; } = "UserTenants";
    public string TenantRolesTableName { get; set; } = "Roles";
    public string TenantInvitesTableName { get; set; } = "TenantInvites";
    public string OtpChallengesTableName { get; set; } = "OtpChallenges";
    public string AuthIdentifiersTableName { get; set; } = "AuthIdentifiers";
    public string ExternalLoginsTableName { get; set; } = "ExternalLogins";
    public string AuthSessionsTableName { get; set; } = "AuthSessions";
    public string OAuthClientsTableName { get; set; } = "OAuthClients";
    public string OAuthAuthorizationCodesTableName { get; set; } = "OAuthAuthorizationCodes";
    public string OAuthConsentsTableName { get; set; } = "OAuthConsents";
    public string ApiCredentialsTableName { get; set; } = "ApiCredentials";
    public string AgentUsersTableName { get; set; } = "AgentUsers";
    public string AgentUserCredentialsTableName { get; set; } = "AgentUserCredentials";
    public string AccessCatalogOverridesTableName { get; set; } = "AccessCatalogOverrides";
    public string AuthAttemptsTableName { get; set; } = "AuthAttempts";
    public string SystemLogsTableName { get; set; } = "SystemLogs";
    public string SystemErrorsTableName { get; set; } = "SystemErrors";

    // ----- Table name helper -----
    public string FullTableName(string baseName)
        => $"{TablePrefix}{baseName}";

    // ----- Key helpers (membership tables) -----
    // UserTenants: PK = "USR|{userId}", RK = "TEN|{tenantId}"
    public string UserTenantsPk(string userId) => $"USR|{NormalizeId(userId)}";
    public string UserTenantsRk(Guid tenantId) => $"TEN|{tenantId:D}";

    public bool TryParseTenantIdFromUserTenantsRk(string rowKey, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(rowKey)) return false;
        if (rowKey.StartsWith("TEN|", StringComparison.OrdinalIgnoreCase))
            return Guid.TryParse(rowKey.Substring(4), out tenantId);
        if (rowKey.StartsWith("TEN#", StringComparison.OrdinalIgnoreCase))
            return Guid.TryParse(rowKey.Substring(4), out tenantId);

        return false;
    }

    // TenantUsers: PK = "TEN|{tenantId}", RK = "USR|{userId}"
    public string TenantUsersPk(Guid tenantId) => $"TEN|{tenantId:D}";
    public string TenantUsersRk(string userId) => $"USR|{NormalizeId(userId)}";

    public bool TryParseUserIdFromTenantUsersRk(string rowKey, out string userId)
    {
        userId = string.Empty;
        if (string.IsNullOrWhiteSpace(rowKey)) return false;
        if (rowKey.StartsWith("USR|", StringComparison.OrdinalIgnoreCase) ||
            rowKey.StartsWith("USR#", StringComparison.OrdinalIgnoreCase))
        {
            userId = rowKey.Substring(4);
            return !string.IsNullOrWhiteSpace(userId);
        }

        return !string.IsNullOrWhiteSpace(userId);
    }

    // Roles: PK = "TEN|{tenantId}", RK = "ROL|{roleId}"
    public string TenantRolesPk(Guid tenantId) => $"TEN|{tenantId:D}";
    public string TenantRolesRk(Guid roleId) => $"ROL|{roleId:D}";

    // AccessCatalogOverrides: PK = "TEN|{tenantId}", RK = "CAT|{catalogItemId}"
    public string AccessCatalogOverridesPk(Guid tenantId) => $"TEN|{tenantId:D}";
    public string AccessCatalogOverridesRk(Guid catalogItemId) => $"CAT|{catalogItemId:D}";

    // ApiCredentials: PK = "TEN|{tenantId}", RK = "CRED|{credentialId}"
    public string ApiCredentialsPk(Guid tenantId) => $"TEN|{tenantId:D}";
    public string ApiCredentialsRk(Guid credentialId) => $"CRED|{credentialId:D}";

    // OAuth client ids and resources may be URLs, so their Azure row keys use stable hashes.
    public const string OAuthClientsPk = "OAUTHCLIENT";
    public string OAuthClientsRk(string clientId) => $"CLIENT|{StableKeyHash(clientId)}";
    public string OAuthAuthorizationCodesPk(string codeHash) => $"OAUTHCODE|{StableKeyHash(codeHash)[..2]}";
    public string OAuthAuthorizationCodesRk(string codeHash) => StableKeyHash(codeHash);
    public string OAuthConsentsPk(Guid tenantId, Guid userId) => $"TEN|{tenantId:D}|USR|{userId:D}";
    public string OAuthConsentsRk(string clientId, string resource) =>
        $"CONSENT|{StableKeyHash($"{clientId.Trim()}\n{resource.Trim()}")}";

    // AgentUsers: PK = "TEN|{tenantId}", RK = "AGU|{agentUserId}"
    public string AgentUsersPk(Guid tenantId) => $"TEN|{tenantId:D}";
    public string AgentUsersRk(Guid agentUserId) => $"AGU|{agentUserId:D}";

    // AgentUserCredentials: PK = "TEN|{tenantId}|AGU|{agentUserId}", RK = "CRED|{credentialId}"
    // Credential lookup index rows share the table: PK = "TEN|{tenantId}|CREDIDX", RK = "CRED|{credentialId}"
    public string AgentUserCredentialsPk(Guid tenantId, Guid agentUserId) => $"TEN|{tenantId:D}|AGU|{agentUserId:D}";
    public string AgentUserCredentialsRk(Guid credentialId) => $"CRED|{credentialId:D}";
    public string AgentCredentialIndexPk(Guid tenantId) => $"TEN|{tenantId:D}|CREDIDX";
    public string AgentCredentialIndexRk(Guid credentialId) => $"CRED|{credentialId:D}";

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
        TenantRolesTableName = NormalizeOrDefault(TenantRolesTableName, "Roles");
        TenantInvitesTableName = NormalizeOrDefault(TenantInvitesTableName, "TenantInvites");
        OtpChallengesTableName = NormalizeOrDefault(OtpChallengesTableName, "OtpChallenges");
        AuthIdentifiersTableName = NormalizeOrDefault(AuthIdentifiersTableName, "AuthIdentifiers");
        ExternalLoginsTableName = NormalizeOrDefault(ExternalLoginsTableName, "ExternalLogins");
        AuthSessionsTableName = NormalizeOrDefault(AuthSessionsTableName, "AuthSessions");
        OAuthClientsTableName = NormalizeOrDefault(OAuthClientsTableName, "OAuthClients");
        OAuthAuthorizationCodesTableName = NormalizeOrDefault(OAuthAuthorizationCodesTableName, "OAuthAuthorizationCodes");
        OAuthConsentsTableName = NormalizeOrDefault(OAuthConsentsTableName, "OAuthConsents");
        ApiCredentialsTableName = NormalizeOrDefault(ApiCredentialsTableName, "ApiCredentials");
        AgentUsersTableName = NormalizeOrDefault(AgentUsersTableName, "AgentUsers");
        AgentUserCredentialsTableName = NormalizeOrDefault(AgentUserCredentialsTableName, "AgentUserCredentials");
        AccessCatalogOverridesTableName = NormalizeOrDefault(AccessCatalogOverridesTableName, "AccessCatalogOverrides");
        AuthAttemptsTableName = NormalizeOrDefault(AuthAttemptsTableName, "AuthAttempts");
        SystemLogsTableName = NormalizeOrDefault(SystemLogsTableName, "SystemLogs");
        SystemErrorsTableName = NormalizeOrDefault(SystemErrorsTableName, "SystemErrors");

        // Validate base table names (prefix is not validated here; it becomes part of final name)
        ValidateTableName(IndexTableName, nameof(IndexTableName));
        ValidateTableName(UserTableName, nameof(UserTableName));
        ValidateTableName(RoleTableName, nameof(RoleTableName));

        ValidateTableName(TenantsTableName, nameof(TenantsTableName));
        ValidateTableName(TenantUsersTableName, nameof(TenantUsersTableName));
        ValidateTableName(UserTenantsTableName, nameof(UserTenantsTableName));
        ValidateTableName(TenantRolesTableName, nameof(TenantRolesTableName));
        ValidateTableName(TenantInvitesTableName, nameof(TenantInvitesTableName));
        ValidateTableName(OtpChallengesTableName, nameof(OtpChallengesTableName));
        ValidateTableName(AuthIdentifiersTableName, nameof(AuthIdentifiersTableName));
        ValidateTableName(ExternalLoginsTableName, nameof(ExternalLoginsTableName));
        ValidateTableName(AuthSessionsTableName, nameof(AuthSessionsTableName));
        ValidateTableName(OAuthClientsTableName, nameof(OAuthClientsTableName));
        ValidateTableName(OAuthAuthorizationCodesTableName, nameof(OAuthAuthorizationCodesTableName));
        ValidateTableName(OAuthConsentsTableName, nameof(OAuthConsentsTableName));
        ValidateTableName(ApiCredentialsTableName, nameof(ApiCredentialsTableName));
        ValidateTableName(AgentUsersTableName, nameof(AgentUsersTableName));
        ValidateTableName(AgentUserCredentialsTableName, nameof(AgentUserCredentialsTableName));
        ValidateTableName(AccessCatalogOverridesTableName, nameof(AccessCatalogOverridesTableName));
        ValidateTableName(AuthAttemptsTableName, nameof(AuthAttemptsTableName));
        ValidateTableName(SystemLogsTableName, nameof(SystemLogsTableName));
        ValidateTableName(SystemErrorsTableName, nameof(SystemErrorsTableName));

        // Validate full table names too (prefix+name must still be valid)
        ValidateTableName(FullTableName(IndexTableName), nameof(TablePrefix) + "+" + nameof(IndexTableName));
        ValidateTableName(FullTableName(UserTableName), nameof(TablePrefix) + "+" + nameof(UserTableName));
        ValidateTableName(FullTableName(RoleTableName), nameof(TablePrefix) + "+" + nameof(RoleTableName));

        ValidateTableName(FullTableName(TenantsTableName), nameof(TablePrefix) + "+" + nameof(TenantsTableName));
        ValidateTableName(FullTableName(TenantUsersTableName), nameof(TablePrefix) + "+" + nameof(TenantUsersTableName));
        ValidateTableName(FullTableName(UserTenantsTableName), nameof(TablePrefix) + "+" + nameof(UserTenantsTableName));
        ValidateTableName(FullTableName(TenantRolesTableName), nameof(TablePrefix) + "+" + nameof(TenantRolesTableName));
        ValidateTableName(FullTableName(TenantInvitesTableName), nameof(TablePrefix) + "+" + nameof(TenantInvitesTableName));
        ValidateTableName(FullTableName(OtpChallengesTableName), nameof(TablePrefix) + "+" + nameof(OtpChallengesTableName));
        ValidateTableName(FullTableName(AuthIdentifiersTableName), nameof(TablePrefix) + "+" + nameof(AuthIdentifiersTableName));
        ValidateTableName(FullTableName(ExternalLoginsTableName), nameof(TablePrefix) + "+" + nameof(ExternalLoginsTableName));
        ValidateTableName(FullTableName(AuthSessionsTableName), nameof(TablePrefix) + "+" + nameof(AuthSessionsTableName));
        ValidateTableName(FullTableName(OAuthClientsTableName), nameof(TablePrefix) + "+" + nameof(OAuthClientsTableName));
        ValidateTableName(FullTableName(OAuthAuthorizationCodesTableName), nameof(TablePrefix) + "+" + nameof(OAuthAuthorizationCodesTableName));
        ValidateTableName(FullTableName(OAuthConsentsTableName), nameof(TablePrefix) + "+" + nameof(OAuthConsentsTableName));
        ValidateTableName(FullTableName(ApiCredentialsTableName), nameof(TablePrefix) + "+" + nameof(ApiCredentialsTableName));
        ValidateTableName(FullTableName(AgentUsersTableName), nameof(TablePrefix) + "+" + nameof(AgentUsersTableName));
        ValidateTableName(FullTableName(AgentUserCredentialsTableName), nameof(TablePrefix) + "+" + nameof(AgentUserCredentialsTableName));
        ValidateTableName(FullTableName(AccessCatalogOverridesTableName), nameof(TablePrefix) + "+" + nameof(AccessCatalogOverridesTableName));
        ValidateTableName(FullTableName(AuthAttemptsTableName), nameof(TablePrefix) + "+" + nameof(AuthAttemptsTableName));
        ValidateTableName(FullTableName(SystemLogsTableName), nameof(TablePrefix) + "+" + nameof(SystemLogsTableName));
        ValidateTableName(FullTableName(SystemErrorsTableName), nameof(TablePrefix) + "+" + nameof(SystemErrorsTableName));
    }

    private static string NormalizeOrDefault(string value, string @default)
        => string.IsNullOrWhiteSpace(value) ? @default : value.Trim();

    private static string NormalizeId(string id)
        => (id ?? string.Empty).Trim();

    private static string StableKeyHash(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

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
