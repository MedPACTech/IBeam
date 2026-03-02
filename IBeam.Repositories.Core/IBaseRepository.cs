namespace IBeam.Repositories.Abstractions;

public interface IBaseRepository<T> where T : class, IEntity
{
    IReadOnlyList<T> GetAll(bool includeArchived = false, bool includeDeleted = false);
    T? GetById(Guid id, bool includeArchived = false, bool includeDeleted = false);
    IReadOnlyList<T> GetByIds(IReadOnlyList<Guid> ids, bool includeArchived = false, bool includeDeleted = false);

    T Save(T entity);
    IReadOnlyList<T> SaveAll(IReadOnlyList<T> entities);

    bool Archive(Guid id);

    bool ArchiveAll(IReadOnlyList<Guid> ids);

    bool Unarchive(Guid id);

    bool UnarchiveAll(IReadOnlyList<Guid> ids);

    void Delete(Guid id);

    void DeleteAll(IReadOnlyList<Guid> ids);
}