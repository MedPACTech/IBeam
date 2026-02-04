namespace IBeam.Repositories.Abstractions;

public interface IRepositoryStore<T> where T : class, IEntity
{
    Task<T?> GetByIdAsync(Guid id);
    Task<IReadOnlyList<T>> GetByIdsAsync(IEnumerable<Guid> ids);
    Task<IReadOnlyList<T>> GetAllAsync();

    Task<T> UpsertAsync(T entity);
    Task<IReadOnlyList<T>> UpsertAllAsync(IEnumerable<T> entities);

    Task HardDeleteAsync(Guid id);
}
