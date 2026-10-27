using System.Text.Json.Serialization;
namespace IBeam.Management.Services.CustomerIdentity;

public sealed class ManagementPortalCustomerDirectoryOptions
{
    public const string SectionName = "IBeam:Management:PortalCustomerIdentity";
    [JsonIgnore] public string ProtectionKey { get; set; } = "";
    public int TokenLifetimeMinutes { get; set; } = 15;
    public int TimeoutSeconds { get; set; } = 20;
    public int MaximumResponseBytes { get; set; } = 1048576;
    public bool IsValid() { try { return Convert.FromBase64String(ProtectionKey).Length == 32 && TokenLifetimeMinutes is >= 1 and <= 60 &&
        TimeoutSeconds is >= 1 and <= 30 && MaximumResponseBytes is >= 32768 and <= 2097152; } catch { return false; } }
    public override string ToString() => "Portal customer directory options (key redacted)";
}
internal static class ManagementCustomerDirectoryResource
{
    public static (string Operation, string Path, string Name) For<T>(Guid? tenantId) where T : class
    {
        if (typeof(T) == typeof(IBeam.Management.CustomerIdentity.ManagementCustomerTenantDto) && tenantId is null)
            return (IBeam.Management.CustomerIdentity.ManagementCustomerIdentityOperations.TenantsRead, "tenants", "tenants");
        if (tenantId is not Guid tenant || tenant == Guid.Empty) throw new IBeam.Management.CustomerIdentity.ManagementCustomerIdentityInvalidRequestException();
        if (typeof(T) == typeof(IBeam.Management.CustomerIdentity.ManagementCustomerUserDto))
            return (IBeam.Management.CustomerIdentity.ManagementCustomerIdentityOperations.UsersRead, "tenants/" + tenant.ToString("D") + "/users", "users");
        if (typeof(T) == typeof(IBeam.Management.CustomerIdentity.ManagementCustomerInvitationDto))
            return (IBeam.Management.CustomerIdentity.ManagementCustomerIdentityOperations.InvitesRead, "tenants/" + tenant.ToString("D") + "/invites", "invites");
        throw new IBeam.Management.CustomerIdentity.ManagementCustomerIdentityInvalidRequestException();
    }
}
