using System.Text.Json.Serialization;
namespace IBeam.Management.CustomerIdentity;

public sealed record ManagementCustomerTenantReadEntity(string RowKey, string? Name, string? NormalizedName, string? Status,
    DateTimeOffset? CreatedUtc, DateTimeOffset? UpdatedUtc, string Version);
public sealed record ManagementCustomerTenantReadPage(IReadOnlyList<ManagementCustomerTenantReadEntity> Items, [property: JsonIgnore] string? ContinuationToken)
{
    public override string ToString() => "Customer tenant provider page (state redacted)";
}
/// <summary>Read-only, entity-focused port into one explicitly supplied target tenant table.</summary>
public interface IManagementCustomerTenantReadRepository
{
    string SourceId { get; }
    Task<ManagementCustomerTenantReadPage> ReadPageAsync(int pageSize, string? continuationToken, CancellationToken ct = default);
    Task<ManagementCustomerTenantReadEntity?> FindAsync(Guid tenantId, CancellationToken ct = default);
}
public interface IManagementTenantDirectoryService : IManagementTenantDirectoryQuery;
public sealed class ManagementCustomerIdentityInvalidRequestException() : Exception("The customer management request is invalid.");
public sealed class ManagementCustomerIdentityNotFoundException() : Exception("The customer management resource was not found.");
public sealed class ManagementCustomerIdentityUnavailableException() : Exception("Customer management is temporarily unavailable.");
