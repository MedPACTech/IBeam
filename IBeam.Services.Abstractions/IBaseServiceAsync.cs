using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IBeam.Services.Abstractions
{
    public interface IBaseServiceAsync<TEntity, TModel>
        where TEntity : class
        where TModel : class
    {
        // Mapping exposure
        TEntity ToEntity(TModel model);
        TModel ToModel(TEntity entity);
        IEnumerable<TEntity> ToEntity(IEnumerable<TModel> models);
        IEnumerable<TModel> ToModel(IEnumerable<TEntity> entities);

        // Reads
        Task<IEnumerable<TModel>> GetAllAsync(CancellationToken ct = default);
        Task<IEnumerable<TModel>> GetAllWithArchivedAsync(bool includeArchived = true, CancellationToken ct = default);
        Task<TModel> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<IEnumerable<TModel>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);

        // Writes
        Task<TModel> SaveAsync(TModel model, CancellationToken ct = default);
        Task<IEnumerable<TModel>> SaveAllAsync(IEnumerable<TModel> models, CancellationToken ct = default);

        // Archive / delete
        Task ArchiveAsync(Guid id, CancellationToken ct = default);
        Task ArchiveAsync(TModel model, CancellationToken ct = default);
        Task ArchiveAllAsync(IEnumerable<TModel> models, CancellationToken ct = default);

        Task UnarchiveAsync(Guid id, CancellationToken ct = default);
        Task UnarchiveAsync(TModel model, CancellationToken ct = default);
        Task UnarchiveAllAsync(IEnumerable<TModel> models, CancellationToken ct = default);

        Task DeleteAsync(Guid id, CancellationToken ct = default);
    }
}
