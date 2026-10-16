using Azure.Data.Tables;
using IBeam.AccessControl.Repositories.AzureTable;
using IBeam.Identity.Repositories.AzureTable.Options;
using Microsoft.Extensions.Configuration;

namespace IBeam.Management.Identity.Services;

/// <summary>Builds an isolated configuration snapshot. Never mutates the customer's configuration.</summary>
public static class ManagementIdentityConfiguration
{
    public static IConfigurationRoot Build(IConfiguration source)
    {
        var management = source.GetSection(ManagementIdentityOptions.SectionName);
        var storage = management.GetSection("Storage").Get<ManagementIdentityStorageOptions>() ?? new();
        if (!Enum.IsDefined(storage.Mode) || string.IsNullOrWhiteSpace(storage.TablePrefix))
            throw new InvalidOperationException("A valid management storage mode and nonempty table prefix are required.");

        var customer = source.GetSection(AzureTableIdentityOptions.SectionName).Get<AzureTableIdentityOptions>() ?? new();
        var customerConnection = customer.StorageConnectionString;
        var connection = storage.ConnectionString;
        if (storage.Mode == ManagementIdentityStorageMode.Shared)
        {
            if (string.IsNullOrWhiteSpace(customerConnection))
                throw new InvalidOperationException("Shared storage requires the existing Identity storage connection for isolation checks.");
            if (string.IsNullOrWhiteSpace(connection)) connection = customerConnection;
            if (!SameEndpoint(connection, customerConnection))
                throw new InvalidOperationException("Shared storage must use the existing Identity storage endpoint.");
        }
        else if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Separate management storage requires its own explicit connection.");

        if (storage.Mode == ManagementIdentityStorageMode.Separate &&
            !string.IsNullOrWhiteSpace(customerConnection) && SameEndpoint(connection, customerConnection))
            throw new InvalidOperationException("Separate management storage cannot use the customer storage endpoint; select Shared explicitly.");

        var identity = new AzureTableIdentityOptions
        {
            StorageConnectionString = connection,
            TablePrefix = storage.TablePrefix,
            CreateTablesIfNotExists = storage.ProvisionSchema
        };
        identity.Validate();
        var access = new AzureTableAccessControlOptions
        {
            StorageConnectionString = connection,
            TablePrefix = storage.TablePrefix,
            CreateTablesIfNotExists = storage.ProvisionSchema
        };
        access.Validate();

        if (storage.Mode == ManagementIdentityStorageMode.Shared)
        {
            var customerAccess = source.GetSection(AzureTableAccessControlOptions.SectionName).Get<AzureTableAccessControlOptions>() ?? new();
            var existingNames = TableNames(customer).Concat(TableNames(customerAccess)).Append(customer.TablePrefix + "Schema");
            var managementNames = TableNames(identity).Concat(TableNames(access)).Append(identity.TablePrefix + "Schema");
            if (existingNames.Intersect(managementNames, StringComparer.OrdinalIgnoreCase).Any())
                throw new InvalidOperationException("Management and customer identity/access-control table names overlap.");
        }

        // Copy management-only Identity settings; never inherit customer JWT/OTP/OAuth settings.
        var values = management.AsEnumerable()
            .Where(x => x.Value is not null)
            .ToDictionary(x => "IBeam:Identity" + x.Key[ManagementIdentityOptions.SectionName.Length..], x => x.Value,
                StringComparer.OrdinalIgnoreCase);
        foreach (var property in typeof(AzureTableIdentityOptions).GetProperties()
            .Where(p => p.PropertyType == typeof(string) && p.Name.EndsWith("TableName", StringComparison.Ordinal)))
            values[AzureTableIdentityOptions.SectionName + ":" + property.Name] = (string)property.GetValue(identity)!;
        values[AzureTableIdentityOptions.SectionName + ":StorageConnectionString"] = connection;
        values[AzureTableIdentityOptions.SectionName + ":TablePrefix"] = identity.TablePrefix;
        values[AzureTableIdentityOptions.SectionName + ":CreateTablesIfNotExists"] = storage.ProvisionSchema.ToString();
        values[AzureTableAccessControlOptions.SectionName + ":StorageConnectionString"] = connection;
        values[AzureTableAccessControlOptions.SectionName + ":TablePrefix"] = access.TablePrefix;
        values[AzureTableAccessControlOptions.SectionName + ":CreateTablesIfNotExists"] = storage.ProvisionSchema.ToString();
        values["IBeam:Identity:Otp:AllowAutoProvisionForUnknownUser"] = "false";
        values["IBeam:Identity:TenantProvisioning:Mode"] = "RequireExistingTenant";
        values["IBeam:Identity:TenantProvisioning:DefaultTenantId"] = management["OrganizationId"];
        values["IBeam:Identity:TenantProvisioning:AutoLinkUserToDefaultTenant"] = "false";
        values["IBeam:Identity:Features:OAuth"] = "false";
        values["IBeam:Identity:Features:PasswordAuth"] = "true";
        values["IBeam:Identity:Features:TwoFactor"] = "true";
        values["IBeam:Identity:Features:ClaimsEnrichment"] = "true";
        values["IBeam:Identity:Events:StrictPublishFailures"] = "true";
        values.TryAdd("IBeam:Identity:Jwt:Audience", "ibeam-management");
        values.TryAdd("IBeam:Identity:Jwt:Issuer", "IBeam.Management.Identity");
        values.TryAdd("IBeam:Identity:Jwt:AccessTokenMinutes", "10");
        values.TryAdd("IBeam:Identity:Jwt:SessionInactivityMinutes", "30");
        values.TryAdd("IBeam:Identity:Jwt:SessionAbsoluteLifetimeDays", "1");
        if (values["IBeam:Identity:Jwt:Audience"] == source["IBeam:Identity:Jwt:Audience"])
            throw new InvalidOperationException("Management and customer JWT audiences must differ.");
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static IEnumerable<string> TableNames(object options)
    {
        var prefix = (string)options.GetType().GetProperty("TablePrefix")!.GetValue(options)!;
        return options.GetType().GetProperties()
            .Where(p => p.PropertyType == typeof(string) && p.Name.EndsWith("TableName", StringComparison.Ordinal))
            .Select(p => prefix + (string)p.GetValue(options)!);
    }

    private static bool SameEndpoint(string left, string right)
    {
        try { return new TableServiceClient(left).Uri.GetLeftPart(UriPartial.Path).TrimEnd('/')
            .Equals(new TableServiceClient(right).Uri.GetLeftPart(UriPartial.Path).TrimEnd('/'), StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        { throw new InvalidOperationException("An Identity storage connection is invalid."); }
    }
}
