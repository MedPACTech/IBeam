using IBeam.DataModels.System;
using IBeam.Repositories.Core;
using ServiceStack.OrmLite;
using ServiceStack.Data;

namespace IBeam.Repositories.OrmLite;

public class OrmLiteRepositoryStore<T> : IRepositoryStore<T>
    where T : class, IEntity
{
    private readonly IDbConnectionFactory _dbFactory;

    public OrmLiteRepositoryStore(IDbConnectionFactory dbFactory)
        => _dbFactory = dbFactory;

    public async Task<T?> GetByIdAsync(Guid id)
    {
        using var db = await _dbFactory.OpenAsync();
        return await db.SingleByIdAsync<T>(id);
    }

    public async Task<IReadOnlyList<T>> GetByIdsAsync(IEnumerable<Guid> ids)
    {
        using var db = await _dbFactory.OpenAsync();
        return await db.SelectAsync<T>(x => Sql.In(x.Id, ids));
    }

    public async Task<IReadOnlyList<T>> GetAllAsync()
    {
        using var db = await _dbFactory.OpenAsync();
        return await db.SelectAsync<T>();
    }

    public async Task<T> UpsertAsync(T entity)
    {
        using var db = await _dbFactory.OpenAsync();
        await db.SaveAsync(entity);
        return entity;
    }

    public async Task<IReadOnlyList<T>> UpsertAllAsync(IEnumerable<T> entities)
    {
        var list = entities.ToList();
        using var db = await _dbFactory.OpenAsync();
        await db.SaveAllAsync(list);
        return list;
    }

    public async Task HardDeleteAsync(Guid id)
    {
        using var db = await _dbFactory.OpenAsync();
        await db.DeleteByIdAsync<T>(id);
    }
}
