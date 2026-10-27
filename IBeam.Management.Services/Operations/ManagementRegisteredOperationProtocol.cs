using IBeam.Management.Operations;

namespace IBeam.Management.Services;

public static class ManagementRegisteredOperationProtocol
{
    public static void Validate(ManagementRegisteredOperationInfo info, ManagementOperationRegistry registry)
    {
        if (info is null || !Name(info.ApplicationKey) || !Name(info.Environment) || info.RealmId == Guid.Empty || info.RealmScope != "dedicated-app" ||
            info.ProtocolVersion != 1 || info.Capabilities is null || info.Capabilities.Length > registry.Descriptors.Count ||
            info.Capabilities.Any(x => x is null) || info.Capabilities.Select(x => x.Operation).Distinct(StringComparer.Ordinal).Count() != info.Capabilities.Length)
            throw new ManagementConnectionInvalidException();
        foreach (var capability in info.Capabilities)
        {
            var descriptor = registry.Find(capability.Operation);
            if (descriptor is null || capability.ProtocolVersion != descriptor.ProtocolVersion || capability.RequestSchema != descriptor.Request.Id ||
                capability.RequestSchemaVersion != descriptor.Request.Version || capability.ResultSchema != descriptor.Result.Id ||
                capability.ResultSchemaVersion != descriptor.Result.Version || capability.ReceiptProtocolVersion != descriptor.ReceiptProtocolVersion ||
                capability.DescriptorFingerprint != ManagementOperationRegistry.Fingerprint(descriptor)) throw new ManagementConnectionInvalidException();
        }
        if (info.Capabilities.Any(x => registry.RequiredFor(x.Operation).Any(required => registry.Find(required) is not null && !info.Capabilities.Any(c => c.Operation == required))))
            throw new ManagementConnectionInvalidException();
    }
    private static bool Name(string? value) => value is { Length: > 0 and <= 100 } && !value.Any(char.IsControl) && !string.IsNullOrWhiteSpace(value);
}
