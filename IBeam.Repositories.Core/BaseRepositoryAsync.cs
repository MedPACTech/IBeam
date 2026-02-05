using IBeam.Repositories.Abstractions;
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
public abstract class BaseRepositoryAsync<T> : IBaseRepositoryAsync<T>
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

    protected BaseRepositoryAsync(
        IRepositoryStore<T> store,
        IMemoryCache memoryCache,
        ITenantContext tenantContext,
        RepositoryOptions options)
    {
        Store = store ?? throw new ArgumentNullException(nameof(store));
        MemoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        TenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        Options = options ?? throw new ArgumentNullException(nameof(options));

        RepositoryName = typeof(T).FullName ?? typeof(T).Name;
        RepositoryCacheName = $"{RepositoryName}Cache";
    }

    // Keep cache keys centralized so ClearCache never drifts
    protected virtual string CacheKey_AllGlobal => $"{RepositoryCacheName}:All:Global";

    protected Guid? CurrentTenantIdOrNull()
        => IsTenantSpecific ? TenantContext.TenantId : null;

    public async Task<T?> GetByIdAsync(Guid id, bool includeArchived = false, bool includeDeleted = false, CancellationToken ct = default)
    {
        ValidateTenantId();

        var tenantId = CurrentTenantIdOrNull();
        var entity = await Store.GetByIdAsync(tenantId, id, ct);

        entity = ApplyTenantVisibility(entity);
        entity = ApplyDeletedVisibility(entity, includeDeleted);
        entity = ApplyArchivedVisibility(entity, includeArchived);

        return entity;
    }

    public async Task<IReadOnlyList<T>> GetByIdsAsync(IReadOnlyList<Guid> ids, bool includeArchived = false, bool includeDeleted = false, CancellationToken ct = default)
    {
        ValidateTenantId();
        if (ids is null || ids.Count == 0) return Array.Empty<T>();

        var idList = ids.Where(x => x != Guid.Empty).Distinct().ToList();
        if (idList.Count == 0) return Array.Empty<T>();

        var tenantId = CurrentTenantIdOrNull();
        var items = await Store.GetByIdsAsync(tenantId, idList, ct);

        return items
            .Select(ApplyTenantVisibility)
            .Select(x => ApplyArchivedVisibility(x, includeArchived))
            .Select(x => ApplyDeletedVisibility(x, includeDeleted))
            .Where(x => x != null)
            .Cast<T>()
            .ToList();
    }

    public async Task<IReadOnlyList<T>> GetAllAsync(bool includeArchived = false, bool includeDeleted = false, CancellationToken ct = default)
    {
        ValidateTenantId();

        var canCache = !IsTenantSpecific && Options.EnableCache && !includeArchived && !includeDeleted;

        if (canCache &&
            MemoryCache.TryGetValue(CacheKey_AllGlobal, out IReadOnlyList<T>? cached) &&
            cached is not null)
            return cached;

        var tenantId = CurrentTenantIdOrNull();
        var all = await Store.GetAllAsync(tenantId, ct);

        var filtered = all
            .Select(ApplyTenantVisibility)
            .Select(x => ApplyArchivedVisibility(x, includeArchived))
            .Select(x => ApplyDeletedVisibility(x, includeDeleted))
            .Where(x => x != null)
            .Cast<T>()
            .ToList();

        if (canCache)
        {
            //TODO Add Chaeche Expiration Options Later
            // Use options-based expiration if provided
            //if (Options.CacheDuration.HasValue && Options.CacheDuration.Value > TimeSpan.Zero)
            //    MemoryCache.Set(CacheKey_AllGlobal, filtered, Options.CacheDuration.Value);
            //else
            MemoryCache.Set(CacheKey_AllGlobal, filtered);
        }

        return filtered;
    }

    public async Task<T> SaveAsync(T entity, CancellationToken ct = default)
    {
        ValidateTenantId();
        ArgumentNullException.ThrowIfNull(entity);

        ApplyIdPolicy(entity);
        ApplyTenantPolicy(entity);

        var tenantId = CurrentTenantIdOrNull();
        var saved = await Store.UpsertAsync(tenantId, entity, ct);
        ClearCache();
        return saved;
    }

    public async Task<IReadOnlyList<T>> SaveAllAsync(IReadOnlyList<T> entities, CancellationToken ct = default)
    {
        ValidateTenantId();
        ArgumentNullException.ThrowIfNull(entities);

        var list = entities.Where(e => e != null).ToList();
        foreach (var e in list)
        {
            ApplyIdPolicy(e);
            ApplyTenantPolicy(e);
        }

        var tenantId = CurrentTenantIdOrNull();
        var saved = await Store.UpsertAllAsync(tenantId, list, ct);
        ClearCache();
        return saved;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        ValidateTenantId();

        // allow deleting archived/deleted items too
        var entity = await GetByIdAsync(id, includeArchived: true, includeDeleted: true, ct);
        if (entity is null)
            throw new RepositoryValidationException(RepositoryName, "DeleteAsync",
                $"Entity of type {typeof(T).Name} with Id {id} not found.");

        var tenantId = CurrentTenantIdOrNull();

        // hard delete if soft delete disabled or entity opts in
        if (Options.DisableSoftDelete || entity is IAllowHardDelete || !IsSoftDeletable)
        {
            await Store.HardDeleteAsync(tenantId, id, ct);
            ClearCache();
            return;
        }

        // soft delete path
        if (entity is not IDeletableEntity soft)
            throw new RepositoryValidationException(RepositoryName, "DeleteAsync",
                $"Entity type {typeof(T).Name} expected to implement {nameof(IDeletableEntity)} but does not.");

        soft.IsDeleted = true;
        await Store.UpsertAsync(tenantId, entity, ct);
        ClearCache();
    }

    public async Task DeleteAllAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        ValidateTenantId();

        if (ids is null || ids.Count == 0)
            return;

        var distinctIds = ids.Where(x => x != Guid.Empty).Distinct().ToList();
        if (distinctIds.Count == 0)
            return;

        // Load with widest visibility so deletes work even if already archived/deleted
        var entities = await GetByIdsAsync(distinctIds, includeArchived: true, includeDeleted: true, ct);

        // If you want strict behavior, you can check missing IDs here and throw.
        // For now, "best effort" is usually fine.
        if (entities.Count == 0)
            return;

        var tenantId = CurrentTenantIdOrNull();

        // If soft delete is disabled globally OR entity type doesn't support soft delete
        // we hard delete everything.
        var mustHardDeleteAll = Options.DisableSoftDelete || !IsSoftDeletable;

        if (mustHardDeleteAll)
        {
            await Store.HardDeleteAllAsync(tenantId, entities.Select(e => e.Id).ToList(), ct);

            ClearCache();
            return;
        }

        // If entity opts into hard delete, split it out
        var hardDeleteIds = new List<Guid>();
        var softDeleteEntities = new List<T>();

        foreach (var e in entities)
        {
            if (e is IAllowHardDelete)
            {
                hardDeleteIds.Add(e.Id);
                continue;
            }

            if (e is not IDeletableEntity d)
            {
                // Safety: if IsSoftDeletable is true but cast fails, something is off.
                throw new RepositoryValidationException(RepositoryName, "DeleteAllAsync",
                    $"Entity type {typeof(T).Name} expected to implement {nameof(IDeletableEntity)} but does not.");
            }

            d.IsDeleted = true;
            softDeleteEntities.Add(e);
        }

        if (softDeleteEntities.Count > 0)
            await Store.UpsertAllAsync(tenantId, softDeleteEntities, ct);

        foreach (var id in hardDeleteIds)
            await Store.HardDeleteAsync(tenantId, id, ct);

        ClearCache();
    }


    public async Task<bool> ArchiveAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await RequireEntityAsync(id, ct);

        if (!IsArchivable || entity is not IArchivableEntity archivable)
            throw new RepositoryValidationException(RepositoryName, "ArchiveAsync",
                $"Entity type {typeof(T).Name} is not archivable.");

        archivable.IsArchived = true;
        await SaveAsync(entity, ct);
        return true;
    }

    public async Task<bool> UnarchiveAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await RequireEntityAsync(id, ct);

        if (!IsArchivable || entity is not IArchivableEntity archivable)
            throw new RepositoryValidationException(RepositoryName, "UnarchiveAsync",
                $"Entity type {typeof(T).Name} is not archivable.");

        archivable.IsArchived = false;
        await SaveAsync(entity, ct);
        return true;
    }

    public async Task<bool> ArchiveAllAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        ValidateTenantId();
        var list = ids?.Where(x => x != Guid.Empty).Distinct().ToList() ?? new List<Guid>();
        if (list.Count == 0) return true;

        if (!IsArchivable)
            throw new RepositoryValidationException(RepositoryName, "ArchiveAllAsync",
                $"Entity type {typeof(T).Name} is not archivable.");

        var tenantId = CurrentTenantIdOrNull();
        var entities = await Store.GetByIdsAsync(tenantId, list, ct);

        var visible = entities
            .Select(ApplyTenantVisibility)
            .Where(x => x != null)
            .Cast<T>()
            .ToList();

        foreach (var e in visible)
        {
            ((IArchivableEntity)e).IsArchived = true;
        }

        await SaveAllAsync(visible, ct);
        return true;
    }

    public async Task<bool> UnarchiveAllAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        ValidateTenantId();
        var list = ids?.Where(x => x != Guid.Empty).Distinct().ToList() ?? new List<Guid>();
        if (list.Count == 0) return true;

        if (!IsArchivable)
            throw new RepositoryValidationException(RepositoryName, "UnarchiveAllAsync",
                $"Entity type {typeof(T).Name} is not archivable.");

        var tenantId = CurrentTenantIdOrNull();
        var entities = await Store.GetByIdsAsync(tenantId, list, ct);

        var visible = entities
            .Select(ApplyTenantVisibility)
            .Where(x => x != null)
            .Cast<T>()
            .ToList();

        foreach (var e in visible)
        {
            ((IArchivableEntity)e).IsArchived = false;
        }

        await SaveAllAsync(visible, ct);
        return true;
    }

    // -------------------------
    // Internal helpers
    // -------------------------

    protected void ClearCache()
    {
        if (!Options.EnableCache) return;

        // clear the actual key(s) we set
        MemoryCache.Remove(CacheKey_AllGlobal);
    }

    protected void ValidateTenantId()
    {
        if (IsTenantSpecific && !TenantContext.IsTenantIdSet())
            throw new RepositoryValidationException(RepositoryName, "ValidateTenantId",
                $"TenantId is required for {RepositoryName} but is not set in the current context.");
    }

    protected void ApplyIdPolicy(T entity)
    {
        if (!Options.IdGeneratedByRepository)
        {
            if (entity.Id == Guid.Empty)
                throw new RepositoryValidationException(RepositoryName, "ApplyIdPolicy",
                    "Empty Id is not allowed when Ids are managed externally.");
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
            throw new RepositoryValidationException(RepositoryName, "ApplyTenantPolicy",
                $"Entity of type {typeof(T).Name} does not implement {nameof(ITenantEntity)}, but the repository is tenant-specific.");

        if (!TenantContext.TenantId.HasValue || TenantContext.TenantId.Value == Guid.Empty)
            throw new RepositoryValidationException(RepositoryName, "ApplyTenantPolicy",
                "TenantId is required but TenantContext.TenantId is not set.");

        if (tenantEntity.TenantId == Guid.Empty)
            throw new RepositoryValidationException(RepositoryName, "ApplyTenantPolicy",
                $"Entity TenantId is empty. TenantId must be set to {TenantContext.TenantId.Value:D} before calling {RepositoryName}.");

        if (tenantEntity.TenantId != TenantContext.TenantId.Value)
            throw new RepositoryValidationException(RepositoryName, "ApplyTenantPolicy",
                $"TenantId mismatch. Entity belongs to TenantId {tenantEntity.TenantId:D}, but repository context is TenantId {TenantContext.TenantId.Value:D}.");
    }

    protected T? ApplyTenantVisibility(T? entity)
    {
        if (entity is null) return null;
        if (!IsTenantSpecific) return entity;

        if (entity is not ITenantEntity tenantEntity) return null;
        if (!TenantContext.TenantId.HasValue) return null;

        return tenantEntity.TenantId == TenantContext.TenantId.Value ? entity : null;
    }

    protected T? ApplyArchivedVisibility(T? entity, bool includeArchived)
    {
        if (entity is null) return null;
        if (!IsArchivable || includeArchived) return entity;

        if (entity is IArchivableEntity a && a.IsArchived) return null;
        return entity;
    }

    protected T? ApplyDeletedVisibility(T? entity, bool includeDeleted)
    {
        if (entity is null) return null;
        if (includeDeleted) return entity;
        if (Options.DisableSoftDelete) return entity;

        if (!IsSoftDeletable) return entity;

        if (entity is IDeletableEntity soft && soft.IsDeleted) return null;
        return entity;
    }

    private async Task<T> RequireEntityAsync(Guid id, CancellationToken ct)
    {
        var entity = await GetByIdAsync(id, includeArchived: true, includeDeleted: true, ct);
        if (entity is null)
            throw new KeyNotFoundException($"Entity of type {typeof(T).Name} with Id {id} not found.");
        return entity;
    }
}

