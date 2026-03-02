using Azure;
using Azure.Data.Tables;
using IBeam.Repositories.Abstractions;
using System.Linq.Expressions;

namespace IBeam.Repositories.AzureTables;

public interface IAzureTablesRepositoryStore<T> : IRepositoryStore<T>
    where T : class, IEntity
{
    Task<T> AddAsync(Guid? tenantId, T entity, CancellationToken ct = default);
    Task<T> UpdateAsync(
        Guid? tenantId,
        T entity,
        TableUpdateMode mode = TableUpdateMode.Replace,
        ETag? eTag = null,
        CancellationToken ct = default);

    Task<(IEnumerable<T> Results, string? ContinuationToken)> GetByPartitionPagedAsync(
        string partitionKey,
        int pageSize,
        string? continuationToken = null,
        CancellationToken ct = default,
        string softDeleteProperty = "IsDeleted");

    IAsyncEnumerable<T> QueryAsync(
        Expression<Func<T, bool>>? predicate = null,
        CancellationToken ct = default,
        string softDeleteProperty = "IsDeleted");
}
