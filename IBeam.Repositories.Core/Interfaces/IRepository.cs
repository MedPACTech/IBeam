using IBeam.DataModels.System;

namespace IBeam.Repositories.Core;

public interface IRepository<T> where T : class, IEntity
{
    Task<T?> GetByIdAsync(Guid id);
    Task<IReadOnlyList<T>> GetByIdsAsync(IEnumerable<Guid> ids);
    Task<IReadOnlyList<T>> GetAllAsync(
        bool includeArchived = false,
        bool includeDeleted = false);

    Task<T> SaveAsync(T entity);
    Task<IReadOnlyList<T>> SaveAllAsync(IEnumerable<T> entities);

    Task DeleteByIdAsync(Guid id);

    Task<bool> ArchiveAsync(Guid id);
    Task<bool> UnarchiveAsync(Guid id);
    Task<bool> ArchiveAllAsync(IEnumerable<Guid> ids);
    Task<bool> UnarchiveAllAsync(IEnumerable<Guid> ids);
}
