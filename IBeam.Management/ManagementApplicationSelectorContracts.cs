using IBeam.Management.Entities;
namespace IBeam.Management;
public sealed record ManagementApplicationSelection(Guid ApplicationId, string ApplicationKey, string Name, string Environment, IReadOnlyList<string> Permissions);
public sealed record ManagementApplicationSelectionPage(IReadOnlyList<ManagementApplicationSelection> Items, int PageSize, string? ContinuationToken);
public interface IManagementApplicationSelector
{
    Task<ManagementApplicationSelectionPage> ListAsync(int pageSize = 50, string? continuationToken = null, CancellationToken ct = default);
}
/// <summary>Internal entity service port; does not authorize HTTP callers.</summary>
public interface IManagementApplicationCatalogProvider
{
    Task<ManagementApplicationPage> ListAsync(Guid organizationId, Guid? after, int pageSize, CancellationToken ct = default);
}
public interface IManagementApplicationConnectionStateProvider
{
    Task<IReadOnlyList<string>> CapabilitiesAsync(ManagementApplicationEntity application, CancellationToken ct = default);
}
