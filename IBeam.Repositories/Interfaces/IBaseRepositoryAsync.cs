using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using IBeam.DataModels.System;        // for IDTO
using ServiceStack.OrmLite;           // for SqlExpression<T>

namespace IBeam.Repositories.Interfaces
{
    public interface IBaseRepositoryAsync<T> where T : class, IDTO
    {
        // -------- Core reads --------
        Task<List<T>> GetAllAsync(bool withArchived = false, CancellationToken ct = default);
        Task<List<T>> GetByIdsAsync(List<Guid> ids, CancellationToken ct = default);
        Task<T> GetByIdAsync(Guid id, CancellationToken ct = default);

        // -------- Query helpers --------
        Task<List<T>> QueryWhereAsync(
            Func<SqlExpression<T>, SqlExpression<T>> expressionBuilder,
            bool includeArchived = false,
            bool includeDeleted = false,
            CancellationToken ct = default);

        Task<TResult> WithConnectionAsync<TResult>(
            Func<IDbConnection, Task<TResult>> work,
            CancellationToken ct = default);

        // -------- Writes --------
        Task<T> SaveAsync(T dto, CancellationToken ct = default);
        Task<List<T>> SaveAllAsync(List<T> dtos, CancellationToken ct = default);

        // -------- Archive / Unarchive --------
        Task<bool> ArchiveAsync(T dto, CancellationToken ct = default);
        Task<bool> ArchiveAllAsync(List<T> dtos, CancellationToken ct = default);
        Task<bool> UnarchiveAsync(T dto, CancellationToken ct = default);
        Task<bool> UnarchiveAllAsync(List<T> dtos, CancellationToken ct = default);

        // -------- Delete --------
        Task DeleteByIdAsync(Guid id, CancellationToken ct = default);
    }
}
