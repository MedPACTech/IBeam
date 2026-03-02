using IBeam.Repositories.Abstractions;
using IBeam.Services.Abstractions;
using IBeam.Repositories.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using ServiceException = IBeam.Services.Abstractions.ServiceException;

namespace IBeam.Services.Core
{
    public abstract class BaseService<TEntity, TModel> : IBaseService<TEntity, TModel>
        where TEntity : class, IEntity
        where TModel : class
    {
        protected readonly string _serviceName;
        protected readonly IBaseRepository<TEntity> _repository;
        protected readonly IModelMapper<TEntity, TModel> _mapper;

        protected readonly IAuditService? _audit; // optional
        protected readonly IEntityAuditService<TEntity>? _typedAudit; // optional overlay

        protected virtual bool AllowGetById { get; set; } = true;
        protected virtual bool AllowGetByIds { get; set; } = false;
        protected virtual bool AllowGetAll { get; set; } = false;
        protected virtual bool AllowGetAllWithArchived { get; set; } = false;

        protected virtual bool AllowSave { get; set; } = false;
        protected virtual bool AllowSaveAll { get; set; } = false;

        protected virtual bool AllowArchive { get; set; } = false;
        protected virtual bool AllowUnarchive { get; set; } = false;
        protected virtual bool AllowDelete { get; set; } = false;

        protected BaseService(
            IBaseRepository<TEntity> repository,
            IModelMapper<TEntity, TModel> mapper,
            IAuditService? audit = null)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _audit = audit;
            _typedAudit = audit as IEntityAuditService<TEntity>;
            _serviceName = GetType().Name;
        }

        // ---- Mapping exposure ----
        public TEntity ToEntity(TModel model) => _mapper.ToEntity(model);
        public TModel ToModel(TEntity entity) => _mapper.ToModel(entity);
        public IEnumerable<TEntity> ToEntity(IEnumerable<TModel> models) => _mapper.ToEntity(models);
        public IEnumerable<TModel> ToModel(IEnumerable<TEntity> entities) => _mapper.ToModel(entities);

        // ---- Hooks (override in derived services) ----
        protected virtual IEnumerable<TModel> PostGetAll(IEnumerable<TModel> models) => models;
        protected virtual IEnumerable<TModel> PostGetByIds(IEnumerable<TModel> models) => models;
        protected virtual TModel PostGetById(TModel model) => model;

        protected virtual void PreSave(TModel model, bool isUpdate) { }
        protected virtual void PostSave(TModel model, bool isUpdate) { }

        protected virtual void PreArchive(Guid id) { }
        protected virtual void PostArchive(Guid id) { }

        protected virtual void PreUnarchive(Guid id) { }
        protected virtual void PostUnarchive(Guid id) { }

        protected virtual void PreDelete(Guid id) { }
        protected virtual void PostDelete(Guid id) { }

        // ---- Reads ----
        public virtual IEnumerable<TModel> GetAll()
        {
            if (!AllowGetAll) throw new MethodAccessException($"{nameof(GetAll)} is not allowed.");

            try
            {
                // Assumes IRepository<TEntity>.GetAll() returns non-archived/non-deleted by default.
                var entities = _repository.GetAll();
                return PostGetAll(ToModel(entities).ToList());
            }
            catch (RepositoryException) { throw; }
            catch (RepositoryStoreException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(GetAll), _serviceName); }
        }

        public virtual IEnumerable<TModel> GetAllWithArchived(bool includeArchived = true)
        {
            if (!AllowGetAllWithArchived) throw new MethodAccessException($"{nameof(GetAllWithArchived)} is not allowed.");

            try
            {
                // Assumes IRepository<TEntity>.GetAll(includeArchived) exists (matches your prior pattern).
                // If your IRepository uses options instead, we’ll adapt this call.
                var entities = _repository.GetAll(includeArchived);
                return PostGetAll(ToModel(entities).ToList());
            }
            catch (RepositoryException) { throw; }
            catch (RepositoryStoreException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(GetAllWithArchived), _serviceName); }
        }

        public virtual TModel GetById(Guid id)
        {
            if (!AllowGetById) throw new MethodAccessException($"{nameof(GetById)} is not allowed.");

            try
            {
                var entity = _repository.GetById(id);
                if (entity is null)
                    throw new KeyNotFoundException($"{typeof(TEntity).Name} with id '{id}' was not found.");

                return PostGetById(ToModel(entity));
            }
            catch (KeyNotFoundException) { throw; }
            catch (RepositoryException) { throw; }
            catch (RepositoryStoreException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(GetById), _serviceName); }
        }

        public virtual IEnumerable<TModel> GetByIds(IEnumerable<Guid> ids)
        {
            if (!AllowGetByIds) throw new MethodAccessException($"{nameof(GetByIds)} is not allowed.");

            var list = ids?.ToList() ?? new List<Guid>();
            if (list.Count == 0) return Enumerable.Empty<TModel>();

            try
            {
                var entities = _repository.GetByIds(list);
                return PostGetByIds(ToModel(entities).ToList());
            }
            catch (RepositoryException) { throw; }
            catch (RepositoryStoreException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(GetByIds), _serviceName); }
        }

        // ---- Writes ----
        public virtual TModel Save(TModel model)
        {
            if (!AllowSave) throw new MethodAccessException($"{nameof(Save)} is not allowed.");

            try
            {
                var entityCandidate = ToEntity(model);
                var isUpdate = entityCandidate.Id != Guid.Empty;

                PreSave(model, isUpdate);

                var entity = ToEntity(model); // remap after PreSave in case model changed
                var saved = _repository.Save(entity);

                PostSave(model, isUpdate);

                if (isUpdate) _typedAudit?.LogUpdate(saved);
                else _typedAudit?.LogCreate(saved);

                return ToModel(saved);
            }
            catch (RepositoryException) { throw; }
            catch (RepositoryStoreException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(Save), _serviceName); }
        }

        public virtual IEnumerable<TModel> SaveAll(IEnumerable<TModel> models)
        {
            if (!AllowSaveAll) throw new MethodAccessException($"{nameof(SaveAll)} is not allowed.");

            var list = models?.ToList() ?? new List<TModel>();
            if (list.Count == 0) return Enumerable.Empty<TModel>();

            try
            {
                var isUpdates = new List<bool>(list.Count);
                foreach (var model in list)
                {
                    var isUpdate = ToEntity(model).Id != Guid.Empty;
                    isUpdates.Add(isUpdate);
                    PreSave(model, isUpdate);
                }

                var entities = ToEntity(list).ToList();
                var saved = _repository.SaveAll(entities).ToList();

                for (var i = 0; i < list.Count; i++)
                {
                    PostSave(list[i], isUpdates[i]);
                }

                for (var i = 0; i < saved.Count && i < isUpdates.Count; i++)
                {
                    if (isUpdates[i]) _typedAudit?.LogUpdate(saved[i]);
                    else _typedAudit?.LogCreate(saved[i]);
                }

                return ToModel(saved).ToList();
            }
            catch (RepositoryException) { throw; }
            catch (RepositoryStoreException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(SaveAll), _serviceName); }
        }

        // ---- Archive / delete ----
        public virtual void Archive(Guid id)
        {
            if (!AllowArchive) throw new MethodAccessException($"{nameof(Archive)} is not allowed.");
            try
            {
                PreArchive(id);
                _repository.Archive(id);
                PostArchive(id);
            }
            catch (RepositoryException) { throw; }
            catch (RepositoryStoreException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(Archive), _serviceName); }
        }

        public void Archive(TModel model)
        {
            var id = ToEntity(model).Id;
            Archive(id);
        }

        public void ArchiveAll(IEnumerable<TModel> models)
        {
            if (!AllowArchive) throw new MethodAccessException($"{nameof(ArchiveAll)} is not allowed.");

            var ids = ToEntity(models ?? Array.Empty<TModel>())
                .Select(e => e.Id)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
            if (ids.Count == 0) return;

            try
            {
                foreach (var id in ids) PreArchive(id);
                _repository.ArchiveAll(ids);
                foreach (var id in ids) PostArchive(id);
            }
            catch (RepositoryException) { throw; }
            catch (RepositoryStoreException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(ArchiveAll), _serviceName); }
        }

        public virtual void Unarchive(Guid id)
        {
            if (!AllowUnarchive) throw new MethodAccessException($"{nameof(Unarchive)} is not allowed.");
            try
            {
                PreUnarchive(id);
                _repository.Unarchive(id);
                PostUnarchive(id);
            }
            catch (RepositoryException) { throw; }
            catch (RepositoryStoreException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(Unarchive), _serviceName); }
        }

        public void Unarchive(TModel model)
        {
            var id = ToEntity(model).Id;
            Unarchive(id);
        }

        public void UnarchiveAll(IEnumerable<TModel> models)
        {
            if (!AllowUnarchive) throw new MethodAccessException($"{nameof(UnarchiveAll)} is not allowed.");

            var ids = ToEntity(models ?? Array.Empty<TModel>())
                .Select(e => e.Id)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
            if (ids.Count == 0) return;

            try
            {
                foreach (var id in ids) PreUnarchive(id);
                _repository.UnarchiveAll(ids);
                foreach (var id in ids) PostUnarchive(id);
            }
            catch (RepositoryException) { throw; }
            catch (RepositoryStoreException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(UnarchiveAll), _serviceName); }
        }

        public virtual void Delete(Guid id)
        {
            if (!AllowDelete) throw new MethodAccessException($"{nameof(Delete)} is not allowed.");
            try
            {
                PreDelete(id);

                // If you want “service-level audit on delete”, either:
                // 1) fetch entity first, then delete, then LogDelete(entity)
                // 2) let repositories/audit pipeline handle it
                _repository.Delete(id);

                PostDelete(id);
            }
            catch (RepositoryException) { throw; }
            catch (RepositoryStoreException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(Delete), _serviceName); }
        }
    }
}



