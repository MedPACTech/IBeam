using Azure;
using Azure.Data.Tables;
using IBeam.Repositories.Abstractions;
using IBeam.Repositories.Core;
using Microsoft.Extensions.Caching.Memory;

namespace IBeam.Repositories.AzureTables;

public class AzureTablesRepositoryBase<T> : BaseRepositoryAsync<T>, IAzureTablesRepositoryAsync<T>
    where T : class, IEntity
{
    private readonly IAzureTablesRepositoryStore<T> _azureStore;

    public AzureTablesRepositoryBase(
        IAzureTablesRepositoryStore<T> store,
        IMemoryCache cache,
        ITenantContext tenantContext,
        RepositoryOptions options)
        : base(store, cache, tenantContext, options)
    {
        _azureStore = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<T?> GetByKeysAsync(
        string partitionKey,
        string rowKey,
        CancellationToken ct = default,
        bool includeDeleted = false,
        bool includeArchived = false)
    {
        ValidateTenantId();

        var entity = await _azureStore.GetByKeysAsync(partitionKey, rowKey, ct);
        entity = ApplyTenantVisibility(entity);
        entity = ApplyDeletedVisibility(entity, includeDeleted);
        entity = ApplyArchivedVisibility(entity, includeArchived);
        return entity;
    }

    public virtual Task<T> AddAsync(T entity, CancellationToken ct = default)
        => AddByKeysAsync(entity, ct);

    public virtual Task<T> UpdateAsync(
        T entity,
        TableUpdateMode mode = TableUpdateMode.Replace,
        ETag? eTag = null,
        CancellationToken ct = default)
        => UpdateByKeysAsync(entity, mode, eTag, ct);

    public virtual Task DeleteAsync(string partitionKey, string rowKey, CancellationToken ct = default)
        => DeleteByKeysAsync(partitionKey, rowKey, ct);

    public async Task<T> AddByKeysAsync(T entity, CancellationToken ct = default)
    {
        ValidateTenantId();
        ArgumentNullException.ThrowIfNull(entity);

        ApplyIdPolicy(entity);
        ApplyTenantPolicy(entity);

        var tenantId = CurrentTenantIdOrNull();
        var added = await _azureStore.AddAsync(tenantId, entity, ct);
        ClearCache();
        return added;
    }

    public async Task<T> UpdateByKeysAsync(
        T entity,
        TableUpdateMode mode = TableUpdateMode.Replace,
        ETag? eTag = null,
        CancellationToken ct = default)
    {
        ValidateTenantId();
        ArgumentNullException.ThrowIfNull(entity);

        ApplyIdPolicy(entity);
        ApplyTenantPolicy(entity);

        var tenantId = CurrentTenantIdOrNull();
        var updated = await _azureStore.UpdateAsync(tenantId, entity, mode, eTag, ct);
        ClearCache();
        return updated;
    }

    public async Task DeleteByKeysAsync(string partitionKey, string rowKey, CancellationToken ct = default)
    {
        ValidateTenantId();
        await _azureStore.DeleteByKeysAsync(partitionKey, rowKey, ct);
        ClearCache();
    }
}
