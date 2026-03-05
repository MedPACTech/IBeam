using Azure;
using Azure.Data.Tables;
using IBeam.Repositories.Abstractions;

namespace IBeam.Repositories.AzureTables;

public interface IAzureTablesRepositoryAsync<T> : IBaseRepositoryAsync<T>
    where T : class, IEntity
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<T> AddAsync(T entity, CancellationToken ct = default);

    Task<T> UpdateAsync(
        T entity,
        TableUpdateMode mode = TableUpdateMode.Replace,
        ETag? eTag = null,
        CancellationToken ct = default);

    Task DeleteAsync(string partitionKey, string rowKey, CancellationToken ct = default);

    Task<T?> GetByKeysAsync(
        string partitionKey,
        string rowKey,
        CancellationToken ct = default,
        bool includeDeleted = false,
        bool includeArchived = false);

    Task<T> AddByKeysAsync(T entity, CancellationToken ct = default);

    Task<T> UpdateByKeysAsync(
        T entity,
        TableUpdateMode mode = TableUpdateMode.Replace,
        ETag? eTag = null,
        CancellationToken ct = default);

    Task DeleteByKeysAsync(string partitionKey, string rowKey, CancellationToken ct = default);
}
