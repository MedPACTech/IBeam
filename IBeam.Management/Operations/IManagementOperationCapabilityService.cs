namespace IBeam.Management.Operations;

/// <summary>Registered app-contract metadata, separate from Identity and business-data DTOs.</summary>
public interface IManagementOperationCapabilityService
{
    Task<IReadOnlyList<ManagementOperationCapability>> GetAsync(CancellationToken ct = default);
}
