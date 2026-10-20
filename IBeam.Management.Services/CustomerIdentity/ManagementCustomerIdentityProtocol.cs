using IBeam.Management.CustomerIdentity;
namespace IBeam.Management.Services.CustomerIdentity;

public static class ManagementCustomerIdentityProtocol
{
    public const int Version = 1;
    public const string DedicatedRealm = "dedicated-app";
    public static void Validate(ManagementCustomerIdentityInfo info)
    {
        if (info is null || info.ProtocolVersion != Version || info.OperationReceiptProtocolVersion is < 0 or > 100 || !ValidName(info.ApplicationKey) ||
            !ValidName(info.Environment) || info.IdentityRealmId == Guid.Empty || info.RealmScope != DedicatedRealm ||
            info.Capabilities is not { Count: >= 1 and <= 14 } ||
            info.Capabilities.Distinct(StringComparer.Ordinal).Count() != info.Capabilities.Count ||
            !info.Capabilities.Contains(ManagementCustomerIdentityOperations.Info, StringComparer.Ordinal) ||
            info.Capabilities.Any(x => !ManagementCustomerIdentityOperations.All.Contains(x, StringComparer.Ordinal)))
            throw new ManagementConnectionInvalidException();
    }
    private static bool ValidName(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 100 && !value.Any(char.IsControl);
    public static void ValidateProfile(ManagementTargetProfile profile, string applicationKey, string environment)
    {
        if (profile is null || profile.CustomerIdentity is null) throw new ManagementConnectionInvalidException();
        if (profile.Diagnostics is { } diagnostics)
        {
            ManagementConnectionProtocol.Validate(diagnostics);
            if (diagnostics.ApplicationKey != applicationKey || diagnostics.Environment != environment) throw new ManagementConnectionInvalidException();
        }
        var customer = profile.CustomerIdentity;
        if (customer.Status == ManagementCustomerIdentityStatuses.Available)
        {
            if (customer.Info is null) throw new ManagementConnectionInvalidException();
            Validate(customer.Info);
            if (customer.Info.ApplicationKey != applicationKey || customer.Info.Environment != environment) throw new ManagementConnectionInvalidException();
        }
        else if (customer.Info is not null || customer.Status is not (ManagementCustomerIdentityStatuses.NotInstalled or
            ManagementCustomerIdentityStatuses.Incompatible or ManagementCustomerIdentityStatuses.Unavailable))
            throw new ManagementConnectionInvalidException();
        if (profile.Diagnostics is null && customer.Status != ManagementCustomerIdentityStatuses.Available)
            throw new ManagementConnectionInvalidException();
    }
}
