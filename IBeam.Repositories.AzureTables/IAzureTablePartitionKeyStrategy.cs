using IBeam.Repositories.Abstractions;

namespace IBeam.Repositories.AzureTables;

/// <summary>
/// Defines partition key behavior for an entity stored in Azure Tables.
/// Implementations should provide deterministic write and read patterns to keep point lookups efficient.
/// </summary>
public interface IAzureTablePartitionKeyStrategy<T>
    where T : class, IEntity
{
    /// <summary>
    /// Computes the partition key used for writes.
    /// </summary>
    string GetPartitionKeyForWrite(Guid? tenantId, T entity);

    /// <summary>
    /// Returns candidate partition keys for a point-read/delete by Id.
    /// Return null when the partition cannot be determined and a row-key fallback scan is required.
    /// </summary>
    IReadOnlyList<string>? GetCandidatePartitionsForId(Guid? tenantId, Guid id);

    /// <summary>
    /// Returns partition keys to query for GetAll.
    /// Return null to indicate an unbounded table scan is required.
    /// </summary>
    IReadOnlyList<string>? GetPartitionsForGetAll(Guid? tenantId);
}
