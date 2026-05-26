namespace IBeam.Api.Abstractions;

public interface IGetAllService<TEntity>
{
    Task<IEnumerable<TEntity>> GetAllAsync(CancellationToken ct = default);
}

public interface IGetAllWithArchivedService<TEntity>
{
    Task<IEnumerable<TEntity>> GetAllWithArchivedAsync(bool withArchived, CancellationToken ct = default);
}

public interface IGetByIdService<TEntity, in TKey>
{
    Task<TEntity?> GetByIdAsync(TKey id, CancellationToken ct = default);
}

public interface IGetByIdsService<TEntity, in TKey>
{
    Task<IEnumerable<TEntity>> GetByIdsAsync(IEnumerable<TKey> ids, CancellationToken ct = default);
}

public interface ICreateService<TEntity>
{
    Task<TEntity> CreateAsync(TEntity model, CancellationToken ct = default);
}

public interface IUpdateService<TEntity>
{
    Task<TEntity> UpdateAsync(TEntity model, CancellationToken ct = default);
}

public interface IDeleteService<in TKey>
{
    Task DeleteAsync(TKey id, CancellationToken ct = default);
}
