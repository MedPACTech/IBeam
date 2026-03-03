namespace IBeam.Repositories.Abstractions;

public interface IRepositoryStore<T> where T : class, IEntity
{
    Task<T?> GetByIdAsync(Guid? tenantId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> GetByIdsAsync(Guid? tenantId, IReadOnlyList<Guid> ids, CancellationToken ct = default);
    Task<IReadOnlyList<T>> GetAllAsync(Guid? tenantId, CancellationToken ct = default);

    Task<T> UpsertAsync(Guid? tenantId, T entity, CancellationToken ct = default);
    Task<IReadOnlyList<T>> UpsertAllAsync(Guid? tenantId, IReadOnlyList<T> entities, CancellationToken ct = default);

    Task HardDeleteAsync(Guid? tenantId, Guid id, CancellationToken ct = default);

    Task HardDeleteAllAsync(Guid? tenantId, IReadOnlyList<Guid> ids, CancellationToken ct = default);
}
