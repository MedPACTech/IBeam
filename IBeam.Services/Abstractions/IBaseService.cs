// IBeam.Services/IBaseService.cs
using System;
using System.Collections.Generic;
using IBeam.DataModels.System; // for IDTO

namespace IBeam.Services
{
    /// <summary>
    /// Generic service contract for mapping and CRUD operations over DTOs and API models.
    /// Implemented by framework BaseService and extended by app-specific services.
    /// </summary>
    public interface IBaseService<TDTO, TModel> where TDTO : class, IEntity
    {
        // ---- Mapping ----
        TDTO ToDto(TModel model);
        TModel ToModel(TDTO dto);
        IEnumerable<TDTO> ToDto(IEnumerable<TModel> models);
        IEnumerable<TModel> ToModel(IEnumerable<TDTO> dtos);

        // ---- Reads ----
        IEnumerable<TModel> GetAll();
        /// <summary>
        /// Returns all items, optionally including archived ones (if the entity supports archiving).
        /// </summary>
        IEnumerable<TModel> GetAllWithArchived(bool withArchived = true);
        TModel GetById(Guid id);
        IEnumerable<TModel> GetByIds(IEnumerable<Guid> ids);

        // ---- Writes ----
        TModel Save(TModel model);
        IEnumerable<TModel> SaveAll(IEnumerable<TModel> models);

        // ---- Archive / Unarchive ----
        void Archive(TModel model);
        void ArchiveAll(IEnumerable<TModel> models);
        void Unarchive(TModel model);
        void UnarchiveAll(IEnumerable<TModel> models);

        // ---- Delete ----
        void Delete(Guid id);
    }
}
