namespace IBeam.Management.Operations;

public static class ManagementRegisteredOperations
{
    public const string Info = "management.operations.info";
    public const int ProtocolVersion = 1;
}
/// <summary>App operation metadata, independent of customer Identity/business data.</summary>
public sealed record ManagementRegisteredOperationInfo(string ApplicationKey, string Environment, Guid RealmId,
    string RealmScope, int ProtocolVersion, ManagementOperationCapability[] Capabilities);
public sealed record ManagementRegisteredOperationDiscovery(string Status, ManagementRegisteredOperationInfo? Info = null);
/// <summary>Typed read-only reconciliation for a registered mutation. It must never resume or retry work.</summary>
public interface IManagementOperationReceiptHandler<TResult>
{
    Task<TResult> ReadReceiptAsync(ManagementOperationContext context, CancellationToken ct = default);
}
public interface IManagementRegisteredOperationInfoService
{
    Task<ManagementRegisteredOperationInfo> GetAsync(CancellationToken ct = default);
}
public interface IManagementRegisteredOperationAvailabilityService
{
    Task<ManagementRegisteredOperationDiscovery> GetAsync(Guid applicationId, CancellationToken ct = default);
}
