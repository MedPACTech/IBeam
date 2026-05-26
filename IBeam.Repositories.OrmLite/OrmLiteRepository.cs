using IBeam.Repositories.Abstractions;
using IBeam.Repositories.Core;
using Microsoft.Extensions.Options;
using ServiceStack.Data;
using ServiceStack.OrmLite;

namespace IBeam.Repositories.OrmLite;

public abstract class OrmLiteRepositoryAsync<T> : BaseRepositoryAsync<T>, IOrmLiteRepositoryAsync<T>
    where T : class, IEntity
{
    private readonly IDbConnectionFactory _dbFactory;

    protected OrmLiteRepositoryAsync(
        IRepositoryStore<T> store,
        Microsoft.Extensions.Caching.Memory.IMemoryCache cache,
        ITenantContext tenantContext,
        RepositoryOptions options,
        IDbConnectionFactory dbFactory)
        : base(store, cache, tenantContext, options)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    }

    public async Task<TResult> WithConnectionAsync<TResult>(
        Func<System.Data.IDbConnection, Task<TResult>> work,
        CancellationToken ct = default)
    {
        try
        {
            using var db = await _dbFactory.OpenDbConnectionAsync(ct);
            return await work(db);
        }
        catch (Exception ex)
        {
            // If you have a common exception type: RepositoryStoreException / RepositoryException
            throw new RepositoryStoreException(RepositoryName, "WithConnectionAsync", ex);
        }
    }

    public async Task<IReadOnlyList<T>> QueryWhereAsync(
        Func<SqlExpression<T>, SqlExpression<T>> expressionBuilder,
        bool includeArchived = false,
        bool includeDeleted = false,
        CancellationToken ct = default)
    {
        try
        {
            ValidateTenantId();

            using var db = await _dbFactory.OpenDbConnectionAsync(ct);

            // base expression
            var q = db.From<T>();

            // apply repo policies in expression form
            q = ApplyOrmLiteTenantFilter(q);
            q = ApplyOrmLiteArchivedFilter(q, includeArchived);
            q = ApplyOrmLiteDeletedFilter(q, includeDeleted);

            // caller adds extra filters/sort
            q = expressionBuilder(q);

            var results = await db.SelectAsync(q, token: ct);
            return results;
        }
        catch (Exception ex)
        {
            throw new RepositoryStoreException(RepositoryName, "QueryWhereAsync", ex);
        }
    }

    // -------------------------
    // OrmLite filter helpers
    // -------------------------

    protected virtual SqlExpression<T> ApplyOrmLiteTenantFilter(SqlExpression<T> q)
    {
        if (!IsTenantSpecific) return q;
        if (!TenantContext.TenantId.HasValue || TenantContext.TenantId.Value == Guid.Empty)
            throw new RepositoryValidationException(RepositoryName, "QueryWhereAsync", "TenantId is required.");

        // Assumes T implements ITenantEntity and has TenantId property.
        // OrmLite can build expressions against interface properties if the concrete type has the property.
        return q.Where(x => ((ITenantEntity)x).TenantId == TenantContext.TenantId.Value);
    }

    protected virtual SqlExpression<T> ApplyOrmLiteArchivedFilter(SqlExpression<T> q, bool includeArchived)
    {
        if (!IsArchivable || includeArchived) return q;
        return q.Where(x => ((IArchivableEntity)x).IsArchived == false);
    }

    protected virtual SqlExpression<T> ApplyOrmLiteDeletedFilter(SqlExpression<T> q, bool includeDeleted)
    {
        if (includeDeleted) return q;
        if (Options.DisableSoftDelete) return q;
        if (!IsSoftDeletable) return q;

        return q.Where(x => ((IDeletableEntity)x).IsDeleted == false);
    }
}
