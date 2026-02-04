using System;
using System.Collections.Generic;

namespace IBeam.Services.Abstractions
{
    public interface IBaseService<TEntity, TModel>
        where TEntity : class
        where TModel : class
    {
        // Mapping exposure (useful for app services)
        TEntity ToEntity(TModel model);
        TModel ToModel(TEntity entity);
        IEnumerable<TEntity> ToEntity(IEnumerable<TModel> models);
        IEnumerable<TModel> ToModel(IEnumerable<TEntity> entities);

        // Reads
        IEnumerable<TModel> GetAll();
        IEnumerable<TModel> GetAllWithArchived(bool includeArchived = true);
        TModel GetById(Guid id);
        IEnumerable<TModel> GetByIds(IEnumerable<Guid> ids);

        // Writes
        TModel Save(TModel model);
        IEnumerable<TModel> SaveAll(IEnumerable<TModel> models);

        // Archive / delete
        void Archive(Guid id);
        void Archive(TModel model);
        void ArchiveAll(IEnumerable<TModel> models);

        void Unarchive(Guid id);
        void Unarchive(TModel model);
        void UnarchiveAll(IEnumerable<TModel> models);

        void Delete(Guid id);
    }
}
