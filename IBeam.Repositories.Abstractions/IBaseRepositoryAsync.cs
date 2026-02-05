namespace IBeam.Repositories.Abstractions;

public interface IBaseRepositoryAsync<T> where T : class, IEntity
{
    Task<IReadOnlyList<T>> GetAllAsync(bool includeArchived = false, bool includeDeleted = false, CancellationToken ct = default);
    Task<IReadOnlyList<T>> GetByIdsAsync(IReadOnlyList<Guid> ids, bool includeArchived = false, bool includeDeleted = false, CancellationToken ct = default);
    Task<T?> GetByIdAsync(Guid id, bool includeArchived = false, bool includeDeleted = false, CancellationToken ct = default);

    Task<T> SaveAsync(T entity, CancellationToken ct = default);
    Task<IReadOnlyList<T>> SaveAllAsync(IReadOnlyList<T> entities, CancellationToken ct = default);

    Task<bool> ArchiveAsync(Guid id, CancellationToken ct = default);
    Task<bool> UnarchiveAsync(Guid id, CancellationToken ct = default);
    Task<bool> ArchiveAllAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);
    Task<bool> UnarchiveAllAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task DeleteAllAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);
}

