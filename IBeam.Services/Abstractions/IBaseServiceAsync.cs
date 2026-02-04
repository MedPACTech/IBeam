// IBeam.Services/IAsyncBaseService.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IBeam.DataModels.System;

namespace IBeam.Services
{
    public interface IBaseServiceAsync<TDTO, TModel> where TDTO : class, IEntity
    {
        // Mapping (sync)
        TDTO ToDto(TModel model);
        TModel ToModel(TDTO dto);
        IEnumerable<TDTO> ToDto(IEnumerable<TModel> models);
        IEnumerable<TModel> ToModel(IEnumerable<TDTO> dtos);

        // Reads
        Task<IEnumerable<TModel>> GetAllAsync(CancellationToken ct = default);
        Task<IEnumerable<TModel>> GetAllWithArchivedAsync(bool withArchived = true, CancellationToken ct = default);
        Task<TModel> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<IEnumerable<TModel>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);

        // Writes
        Task<TModel> SaveAsync(TModel model, CancellationToken ct = default);
        Task<IEnumerable<TModel>> SaveAllAsync(IEnumerable<TModel> models, CancellationToken ct = default);

        // Archive / Unarchive
        Task ArchiveAsync(TModel model, CancellationToken ct = default);
        Task ArchiveAllAsync(IEnumerable<TModel> models, CancellationToken ct = default);
        Task UnarchiveAsync(TModel model, CancellationToken ct = default);
        Task UnarchiveAllAsync(IEnumerable<TModel> models, CancellationToken ct = default);

        // Delete
        Task DeleteAsync(Guid id, CancellationToken ct = default);
    }
}
