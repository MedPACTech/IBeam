using IBeam.DataModels.System;
using Microsoft.Extensions.Caching.Memory;

namespace IBeam.Repositories.Core;

/// <summary>
/// Provider-agnostic repository policy layer.
/// Storage engines (OrmLite / AzureTables / EF / Cosmos) implement IRepositoryStore{T}.
/// This class enforces:
/// - Tenant policy (if T : ITenantEntity)
/// - Archive policy (if T : IArchivableEntity)
/// - Soft delete policy (if T : ISoftDeletableEntity) unless hard-delete is enabled
/// - Id policy (IdGeneratedByRepository)
/// - Cache policy (cache only "normal" GetAll results)
/// </summary>
public abstract class RepositoryBase<T> : IRepository<T>
    where T : class, IEntity
{
    protected readonly IRepositoryStore<T> Store;
    protected readonly IMemoryCache MemoryCache;
    protected readonly ITenantContext TenantContext;
    protected readonly RepositoryOptions Options;

    public string RepositoryName { get; }
    public string RepositoryCacheName { get; }

    protected bool IsTenantSpecific => typeof(ITenantEntity).IsAssignableFrom(typeof(T));
    protected bool IsArchivable => typeof(IArchivableEntity).IsAssignableFrom(typeof(T));
    protected bool IsSoftDeletable => typeof(IDeletableEntity).IsAssignableFrom(typeof(T));

    protected RepositoryBase(
        IRepositoryStore<T> store,
        IMemoryCache memoryCache,
        ITenantContext tenantContext,
        RepositoryOptions options)
    {
        Store = store ?? throw new ArgumentNullException(nameof(store));
        MemoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        TenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        Options = options ?? throw new ArgumentNullException(nameof(options));

        // Similar to your current pattern
        RepositoryName = typeof(T).FullName ?? typeof(T).Name;
        RepositoryCacheName = $"{RepositoryName}Cache";

        if (IsTenantSpecific && !TenantContext.IsTenantIdSet())
        {
            throw new InvalidOperationException(
                $"TenantId is required for {RepositoryName} but is not set in the TenantContext.");
        }
    }

    public async Task<T?> GetByIdAsync(Guid id)
    {
        ValidateTenantId();

        var entity = await Store.GetByIdAsync(id);

        // Mimic ApplyCommonFilters behavior: if it's not visible to tenant, treat as not found
        entity = ApplyTenantVisibility(entity);
        entity = ApplyDeletedVisibility(entity, includeDeleted: false);

        // Note: archive visibility is controlled by GetAllAsync parameters, but GetById historically
        // applies common filters (tenant + not archived + not deleted).
        entity = ApplyArchivedVisibility(entity, includeArchived: false);

        if (entity == null)
            return null;

        return entity;
    }

    public async Task<IReadOnlyList<T>> GetByIdsAsync(IEnumerable<Guid> ids)
    {
        ValidateTenantId();

        if (ids == null) return Array.Empty<T>();

        var idList = ids.Where(x => x != Guid.Empty).Distinct().ToList();
        if (idList.Count == 0) return Array.Empty<T>();

        var items = await Store.GetByIdsAsync(idList);

        // Apply common filters like your current ApplyCommonFilters()
        return items
            .Select(x => ApplyTenantVisibility(x))
            .Select(x => ApplyArchivedVisibility(x, includeArchived: false))
            .Select(x => ApplyDeletedVisibility(x, includeDeleted: false))
            .Where(x => x != null)
            .Cast<T>()
            .ToList();
    }

    public async Task<IReadOnlyList<T>> GetAllAsync(bool includeArchived = false, bool includeDeleted = false)
    {
        ValidateTenantId();

        var canCache = Options.EnableCache && !includeArchived && !includeDeleted;

        if (canCache && MemoryCache.TryGetValue(RepositoryCacheName, out IReadOnlyList<T>? cached) && cached != null)
            return cached;

        var all = await Store.GetAllAsync();

        var filtered = all
            .Select(x => ApplyTenantVisibility(x))
            .Select(x => ApplyArchivedVisibility(x, includeArchived))
            .Select(x => ApplyDeletedVisibility(x, includeDeleted))
            .Where(x => x != null)
            .Cast<T>()
            .ToList();

        if (canCache)
            MemoryCache.Set(RepositoryCacheName, filtered);

        return filtered;
    }

    public async Task<T> SaveAsync(T entity)
    {
        ValidateTenantId();
        if (entity == null) throw new ArgumentNullException(nameof(entity));

        ApplyIdPolicy(entity);
        ApplyTenantPolicy(entity);

        var saved = await Store.UpsertAsync(entity);
        ClearCache();
        return saved;
    }

    public async Task<IReadOnlyList<T>> SaveAllAsync(IEnumerable<T> entities)
    {
        ValidateTenantId();
        if (entities == null) throw new ArgumentNullException(nameof(entities));

        var list = entities.ToList();

        foreach (var entity in list)
        {
            if (entity == null) continue;
            ApplyIdPolicy(entity);
            ApplyTenantPolicy(entity);
        }

        var saved = await Store.UpsertAllAsync(list);
        ClearCache();
        return saved;
    }

    public async Task DeleteByIdAsync(Guid id)
    {
        ValidateTenantId();

        // Use GetByIdAsync to enforce filters/visibility like your current code
        var entity = await GetByIdAsync(id);
        if (entity == null)
            throw new KeyNotFoundException($"Entity of type {typeof(T).Name} with Id {id} not found.");

        // Hard delete if:
        // - global option says disable soft delete
        // - entity opts into hard delete (marker)
        // Otherwise soft delete if entity supports it.
        if (Options.DisableSoftDelete || entity is IAllowHardDelete)
        {
            await Store.HardDeleteAsync(id);
            ClearCache();
            return;
        }

        if (entity is IDeletableEntity soft)
        {
            soft.IsDeleted = true;
            await Store.UpsertAsync(entity);
            ClearCache();
            return;
        }

        // If entity doesn't have soft delete flag, fall back to hard delete.
        await Store.HardDeleteAsync(id);
        ClearCache();
    }

    public async Task<bool> ArchiveAsync(Guid id)
    {
        var entity = await RequireEntityAsync(id);

        if (entity is not IArchivableEntity archivable)
            throw new InvalidOperationException($"Entity of type {typeof(T).Name} does not implement {nameof(IArchivableEntity)}.");

        archivable.IsArchived = true;
        await SaveAsync(entity);

        return true;
    }

    public async Task<bool> UnarchiveAsync(Guid id)
    {
        var entity = await RequireEntityAsync(id);

        if (entity is not IArchivableEntity archivable)
            throw new InvalidOperationException($"Entity of type {typeof(T).Name} does not implement {nameof(IArchivableEntity)}.");

        archivable.IsArchived = false;
        await SaveAsync(entity);

        return true;
    }

    public async Task<bool> ArchiveAllAsync(IEnumerable<Guid> ids)
    {
        ValidateTenantId();
        var list = ids?.Where(x => x != Guid.Empty).Distinct().ToList() ?? new List<Guid>();
        if (list.Count == 0) return true;

        var entities = await Store.GetByIdsAsync(list);

        var visible = entities
            .Select(x => ApplyTenantVisibility(x))
            .Where(x => x != null)
            .Cast<T>()
            .ToList();

        foreach (var e in visible)
        {
            if (e is not IArchivableEntity a)
                throw new InvalidOperationException($"Entity of type {typeof(T).Name} does not implement {nameof(IArchivableEntity)}.");

            a.IsArchived = true;
        }

        await SaveAllAsync(visible);
        return true;
    }

    public async Task<bool> UnarchiveAllAsync(IEnumerable<Guid> ids)
    {
        ValidateTenantId();
        var list = ids?.Where(x => x != Guid.Empty).Distinct().ToList() ?? new List<Guid>();
        if (list.Count == 0) return true;

        var entities = await Store.GetByIdsAsync(list);

        var visible = entities
            .Select(x => ApplyTenantVisibility(x))
            .Where(x => x != null)
            .Cast<T>()
            .ToList();

        foreach (var e in visible)
        {
            if (e is not IArchivableEntity a)
                throw new InvalidOperationException($"Entity of type {typeof(T).Name} does not implement {nameof(IArchivableEntity)}.");

            a.IsArchived = false;
        }

        await SaveAllAsync(visible);
        return true;
    }

    // -------------------------
    // Internal helpers
    // -------------------------

    protected void ClearCache()
    {
        if (Options.EnableCache)
            MemoryCache.Remove(RepositoryCacheName);
    }

    protected void ValidateTenantId()
    {
        if (IsTenantSpecific && !TenantContext.IsTenantIdSet())
            throw new InvalidOperationException($"TenantId is required for {RepositoryName} but is not set in the current context.");
    }

    protected void ApplyIdPolicy(T entity)
    {
        if (!Options.IdGeneratedByRepository)
        {
            if (entity.Id == Guid.Empty)
                throw new InvalidOperationException("Empty Id is not allowed when Ids are managed externally.");
        }
        else
        {
            if (entity.Id == Guid.Empty)
                entity.Id = Guid.NewGuid();
        }
    }

    protected void ApplyTenantPolicy(T entity)
    {
        if (!IsTenantSpecific) return;

        if (entity is not ITenantEntity tenantEntity)
            throw new InvalidOperationException($"Entity of type {typeof(T).Name} does not implement {nameof(ITenantEntity)}, but the repository is tenant-specific.");

        if (!TenantContext.TenantId.HasValue)
            throw new InvalidOperationException("TenantId is required but TenantContext.TenantId is null.");

        if (tenantEntity.TenantId == Guid.Empty)
            tenantEntity.TenantId = TenantContext.TenantId.Value;

        if (tenantEntity.TenantId != TenantContext.TenantId.Value)
        {
            throw new InvalidOperationException(
                $"TenantId mismatch. Entity belongs to TenantId {tenantEntity.TenantId}, but repository is for TenantId {TenantContext.TenantId}.");
        }
    }

    protected T? ApplyTenantVisibility(T? entity)
    {
        if (entity == null) return null;

        if (!IsTenantSpecific) return entity;

        if (entity is not ITenantEntity tenantEntity)
            return null;

        if (!TenantContext.TenantId.HasValue)
            return null;

        return tenantEntity.TenantId == TenantContext.TenantId.Value ? entity : null;
    }

    protected T? ApplyArchivedVisibility(T? entity, bool includeArchived)
    {
        if (entity == null) return null;

        if (!IsArchivable || includeArchived) return entity;

        if (entity is not IArchivableEntity a)
            return entity; // if you got here and it doesn't implement, don't hide it

        return a.IsArchived ? null : entity;
    }

    protected T? ApplyDeletedVisibility(T? entity, bool includeDeleted)
    {
        if (entity == null) return null;

        if (includeDeleted) return entity;

        // If soft delete is globally disabled, ignore IsDeleted filtering (matches your current intent)
        if (Options.DisableSoftDelete) return entity;

        if (entity is IDeletableEntity soft)
            return soft.IsDeleted ? null : entity;

        return entity;
    }

    private async Task<T> RequireEntityAsync(Guid id)
    {
        var entity = await GetByIdAsync(id);
        if (entity == null)
            throw new KeyNotFoundException($"Entity of type {typeof(T).Name} with Id {id} not found.");
        return entity;
    }
}
