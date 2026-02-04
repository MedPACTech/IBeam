using IBeam.Repositories.Abstractions;

public abstract class BaseRepository<T> : IBaseRepository<T> where T : class, IEntity
{
    private readonly IBaseRepositoryAsync<T> _async;

    protected BaseRepository(IBaseRepositoryAsync<T> asyncRepo)
        => _async = asyncRepo;

    public IReadOnlyList<T> GetAll(bool includeArchived = false, bool includeDeleted = false)
        => _async.GetAllAsync(includeArchived, includeDeleted).GetAwaiter().GetResult();

    public T? GetById(Guid id, bool includeArchived = false, bool includeDeleted = false)
        => _async.GetByIdAsync(id, includeArchived, includeDeleted).GetAwaiter().GetResult();

    public IReadOnlyList<T> GetByIds(IReadOnlyList<Guid> ids, bool includeArchived = false, bool includeDeleted = false)
        => _async.GetByIdsAsync(ids, includeArchived, includeDeleted).GetAwaiter().GetResult();

    public T Save(T entity)
        => _async.SaveAsync(entity).GetAwaiter().GetResult();

    public IReadOnlyList<T> SaveAll(IReadOnlyList<T> entities)
        => _async.SaveAllAsync(entities).GetAwaiter().GetResult();

    public bool Archive(Guid id)
        => _async.ArchiveAsync(id).GetAwaiter().GetResult();

    public bool Delete(Guid id)
        => _async.DeleteAsync(id).GetAwaiter().GetResult();
}
