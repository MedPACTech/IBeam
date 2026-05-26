using IBeam.Repositories.Abstractions;
using IBeam.Repositories.Core;
using ServiceStack.Data;
using ServiceStack.OrmLite;
using System.Data;

namespace IBeam.Repositories.OrmLite;

public sealed class OrmLiteRepositoryStore<T> : IRepositoryStore<T>
    where T : class, IEntity
{
    private readonly IDbConnectionFactory _dbFactory;

    public OrmLiteRepositoryStore(IDbConnectionFactory dbFactory)
        => _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    // -------- exception translation helpers --------

    private async Task Execute(string op, Func<Task> work)
    {
        try { await work(); }
        catch (Exception ex) // you can narrow to DbException/OrmLiteException if you want
        {
            throw new RepositoryStoreException(typeof(T).Name, op, ex);
        }
    }

    private async Task<TResult> Execute<TResult>(string op, Func<Task<TResult>> work)
    {
        try { return await work(); }
        catch (Exception ex)
        {
            throw new RepositoryStoreException(typeof(T).Name, op, ex);
        }
    }

    // -------- IRepositoryStore<T> --------
    // NOTE: tenantId is intentionally unused here for SQL stores where TenantId is a column.
    // BaseRepository enforces tenant policy & filters and services should stamp TenantId on writes.
    // If you later want DB-side enforcement, you can implement WHERE TenantId = @tenantId here.

    public Task<T?> GetByIdAsync(Guid? tenantId, Guid id, CancellationToken ct = default)
        => Execute("GetByIdAsync", async () =>
        {
            if (id == Guid.Empty) return null;

            using var db = await _dbFactory.OpenAsync(ct);
            return await db.SingleByIdAsync<T>(id, token: ct);
        });

    public Task<IReadOnlyList<T>> GetByIdsAsync(Guid? tenantId, IReadOnlyList<Guid> ids, CancellationToken ct = default)
        => Execute("GetByIdsAsync", async () =>
        {
            var list = ids?.Where(x => x != Guid.Empty).Distinct().ToList() ?? new();
            if (list.Count == 0) return (IReadOnlyList<T>)Array.Empty<T>();

            using var db = await _dbFactory.OpenAsync(ct);
            var result = await db.SelectAsync<T>(x => Sql.In(x.Id, list), token: ct);
            return (IReadOnlyList<T>)result;
        });

    public Task<IReadOnlyList<T>> GetAllAsync(Guid? tenantId, CancellationToken ct = default)
        => Execute("GetAllAsync", async () =>
        {
            using var db = await _dbFactory.OpenAsync(ct);
            var result = await db.SelectAsync<T>(token: ct);
            return (IReadOnlyList<T>)result;
        });

    public Task<T> UpsertAsync(Guid? tenantId, T entity, CancellationToken ct = default)
        => Execute("UpsertAsync", async () =>
        {
            ArgumentNullException.ThrowIfNull(entity);

            using var db = await _dbFactory.OpenAsync(ct);
            await db.SaveAsync(entity, token: ct);
            return entity;
        });

    public Task<IReadOnlyList<T>> UpsertAllAsync(Guid? tenantId, IReadOnlyList<T> entities, CancellationToken ct = default)
        => Execute("UpsertAllAsync", async () =>
        {
            var list = entities?.Where(x => x != null).ToList() ?? new();
            if (list.Count == 0) return (IReadOnlyList<T>)Array.Empty<T>();

            using var db = await _dbFactory.OpenAsync(ct);
            await db.SaveAllAsync(list, token: ct);
            return (IReadOnlyList<T>)list;
        });

    public Task HardDeleteAsync(Guid? tenantId, Guid id, CancellationToken ct = default)
        => Execute("HardDeleteAsync", async () =>
        {
            if (id == Guid.Empty) return;

            using var db = await _dbFactory.OpenAsync(ct);
            await db.DeleteByIdAsync<T>(id, token: ct);
        });

    public Task HardDeleteAllAsync(Guid? tenantId, IReadOnlyList<Guid> ids, CancellationToken ct = default)
        => Execute("HardDeleteAllAsync", async () =>
        {
            var list = ids?.Where(x => x != Guid.Empty).Distinct().ToList() ?? new();
            if (list.Count == 0) return;

            using var db = await _dbFactory.OpenAsync(ct);
            await db.DeleteByIdsAsync<T>(list, token: ct);
        });
}
