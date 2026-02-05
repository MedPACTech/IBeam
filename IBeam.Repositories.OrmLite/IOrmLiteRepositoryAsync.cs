using IBeam.Repositories.Abstractions;
using ServiceStack.OrmLite;
using System;
using System.Collections.Generic;
using System.Text;

namespace IBeam.Repositories.OrmLite;

public interface IOrmLiteRepositoryAsync<T> : IBaseRepositoryAsync<T>
    where T : class, IEntity
{
    Task<IReadOnlyList<T>> QueryWhereAsync(
        Func<SqlExpression<T>, SqlExpression<T>> expressionBuilder,
        bool includeArchived = false,
        bool includeDeleted = false,
        CancellationToken ct = default);

    Task<TResult> WithConnectionAsync<TResult>(
        Func<System.Data.IDbConnection, Task<TResult>> work,
        CancellationToken ct = default);
}