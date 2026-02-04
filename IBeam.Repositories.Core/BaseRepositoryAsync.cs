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

    protected Guid? CurrentTenantIdOrNull()
    => IsTenantSpecific ? TenantContext.TenantId : null;


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

        // Similar to your current pattern
        RepositoryName = typeof(T).FullName ?? typeof(T).Name;
        RepositoryCacheName = $"{RepositoryName}Cache";
    }

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
        if (ids == null) return Array.Empty<T>();

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
        var cacheKey = $"{RepositoryCacheName}:All:Global";

        if (canCache &&
            MemoryCache.TryGetValue(cacheKey, out IReadOnlyList<T>? cached) &&
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
            MemoryCache.Set(cacheKey, filtered); //TODO: Add expiration options?

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


    public async Task DeleteByIdAsync(Guid id, CancellationToken ct = default)
    {
        ValidateTenantId();

        var entity = await GetByIdAsync(id, includeArchived: true, includeDeleted: true, ct);
        if (entity == null)
            throw new RepositoryValidationException(RepositoryName, "DeleteByIdAsync",
                $"Entity of type {typeof(T).Name} with Id {id} not found.");

        var tenantId = CurrentTenantIdOrNull();

        if (Options.DisableSoftDelete || entity is IAllowHardDelete)
        {
            await Store.HardDeleteAsync(tenantId, id, ct);
            ClearCache();
            return;
        }

        if (entity is IDeletableEntity soft)
        {
            soft.IsDeleted = true;
            await Store.UpsertAsync(tenantId, entity, ct);
            ClearCache();
            return;
        }

        await Store.HardDeleteAsync(tenantId, id, ct);
        ClearCache();
    }

    public async Task<bool> ArchiveAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await RequireEntityAsync(id);

        if (entity is not IArchivableEntity archivable)
            throw new RepositoryValidationException(RepositoryName, "ArchiveAsync", $"Entity of type {typeof(T).Name} does not implement {nameof(IArchivableEntity)}.");

        archivable.IsArchived = true;
        await SaveAsync(entity, ct);

        return true;
    }

    public async Task<bool> UnarchiveAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await RequireEntityAsync(id);

        if (entity is not IArchivableEntity archivable)
            throw new RepositoryValidationException(RepositoryName, "UnarchiveAsync", $"Entity of type {typeof(T).Name} does not implement {nameof(IArchivableEntity)}.");

        archivable.IsArchived = false;
        await SaveAsync(entity, ct);

        return true;
    }

    public async Task<bool> ArchiveAllAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        ValidateTenantId();
        var list = ids?.Where(x => x != Guid.Empty).Distinct().ToList() ?? new List<Guid>();
        if (list.Count == 0) return true;

        var tenantId = CurrentTenantIdOrNull(); // helper: tenant if tenant-specific else null
        var entities = await Store.GetByIdsAsync(tenantId, list, ct);

        var visible = entities
            .Select(x => ApplyTenantVisibility(x))
            .Where(x => x != null)
            .Cast<T>()
            .ToList();

        foreach (var e in visible)
        {
            if (e is not IArchivableEntity a)
                throw new RepositoryValidationException(RepositoryName, "ArchiveAllAsync", $"Entity of type {typeof(T).Name} does not implement {nameof(IArchivableEntity)}.");

            a.IsArchived = true;
        }

        await SaveAllAsync(visible);
        return true;
    }

    public async Task<bool> UnarchiveAllAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        ValidateTenantId();
        var list = ids?.Where(x => x != Guid.Empty).Distinct().ToList() ?? new List<Guid>();
        if (list.Count == 0) return true;

        var tenantId = CurrentTenantIdOrNull(); // helper: tenant if tenant-specific else null
        var entities = await Store.GetByIdsAsync(tenantId, list, ct);

        var visible = entities
            .Select(x => ApplyTenantVisibility(x))
            .Where(x => x != null)
            .Cast<T>()
            .ToList();

        foreach (var e in visible)
        {
            if (e is not IArchivableEntity a)
                throw new RepositoryValidationException(RepositoryName, "UnarchiveAllAsync", $"Entity of type {typeof(T).Name} does not implement {nameof(IArchivableEntity)}.");

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
            throw new RepositoryValidationException(RepositoryName, "ValidateTenantId",
                $"TenantId is required for {RepositoryName} but is not set in the current context.");
    }

    protected void ApplyIdPolicy(T entity)
    {
        if (!Options.IdGeneratedByRepository)
        {
            if (entity.Id == Guid.Empty)
                throw new RepositoryValidationException(RepositoryName, "ApplyIdPolicy", "Empty Id is not allowed when Ids are managed externally.");
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
            throw new RepositoryValidationException(
                RepositoryName, "ApplyTenantPolicy",
                $"Entity of type {typeof(T).Name} does not implement {nameof(ITenantEntity)}, but the repository is tenant-specific.");

        if (!TenantContext.TenantId.HasValue || TenantContext.TenantId.Value == Guid.Empty)
            throw new RepositoryValidationException(
                RepositoryName, "ApplyTenantPolicy",
                "TenantId is required but TenantContext.TenantId is not set.");

        // ?? Require services to pass a tenant-stamped entity
        if (tenantEntity.TenantId == Guid.Empty)
            throw new RepositoryValidationException(
                RepositoryName, "ApplyTenantPolicy",
                $"Entity TenantId is empty. TenantId must be set to {TenantContext.TenantId.Value:D} before calling {RepositoryName}.");

        if (tenantEntity.TenantId != TenantContext.TenantId.Value)
            throw new RepositoryValidationException(
                RepositoryName, "ApplyTenantPolicy",
                $"TenantId mismatch. Entity belongs to TenantId {tenantEntity.TenantId:D}, but repository context is TenantId {TenantContext.TenantId.Value:D}.");
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
