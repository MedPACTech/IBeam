using AutoMapper;
using IBeam.DataModels.System;
using IBeam.Models.Interfaces;
using IBeam.Repositories.Interfaces;
using IBeam.Services.Abstractions;
using IBeam.Utilities.Auditing;
using IBeam.Utilities.Exceptions;
using System.Collections.Generic;

namespace IBeam.Services
{
    public abstract class BaseService<TDTO, TModel> : IBaseService<TDTO, TModel>
        where TDTO : class, IEntity 
        where TModel : class, IBaseModel 
    {
        protected readonly string _serviceName;
        protected readonly IBaseRepository<TDTO> _repository;
        protected readonly IMapper _mapper;
        protected readonly IAuditService _audit;                   // generic fallback
        protected readonly IEntityAuditService<TDTO>? _typedAudit; // per-entity overlay

        /// <summary>Allow retrieving a single entity by Id (default: true to support new model creation).</summary>
        protected virtual bool AllowGetById { get; set; } = true;

        /// <summary>Allow retrieving multiple entities by Ids.</summary>
        protected virtual bool AllowGetByIds { get; set; } = false;

        /// <summary>Allow retrieving all entities (non-archived by default).</summary>
        protected virtual bool AllowGetAll { get; set; } = false;

        /// <summary>Allow retrieving all entities with archived toggle.</summary>
        protected virtual bool AllowGetAllWithArchived { get; set; } = false;

        /// <summary>Allow saving (create/update) a single entity.</summary>
        protected virtual bool AllowSave { get; set; } = false;

        /// <summary>Allow saving multiple entities at once.</summary>
        protected virtual bool AllowSaveAll { get; set; } = false;

        /// <summary>Allow archiving/unarchiving records.</summary>
        protected virtual bool AllowArchive { get; set; } = false;

        /// <summary>Allow deleting an entity by Id.</summary>
        protected virtual bool AllowDelete { get; set; } = false;

        /// <summary>Allow returning a new model instance when an empty Id is requested.</summary>
        protected virtual bool AllowNewModel { get; private set; } = true;

        //TODO: IAudit, IMapper, IBaseRepository<TDTO> should be injected via DI container, not constructor
        //TODO: Can these services be nullable ? If so, we need to handle that in the methods
        protected BaseService(
            IBaseRepository<TDTO> repository,
            IAuditService auditService,
            IMapper mapper,
            IEnumerable<IEntityAuditService<TDTO>> typedAudits // resolves to empty if none
            )
        {
            _serviceName = GetType().Name; // e.g., PatientService
            _repository = repository;
            _audit = auditService;
            _mapper = mapper;
            _typedAudit = _audit as IEntityAuditService<TDTO>; // typedAudits.FirstOrDefault(); // null if not registered
        }

        #region Mapper Methods

        public TDTO ToDto(TModel model) => _mapper.Map<TDTO>(model);
        public TModel ToModel(TDTO dto) => _mapper.Map<TModel>(dto);
        public IEnumerable<TDTO> ToDto(IEnumerable<TModel> models) => _mapper.Map<IEnumerable<TDTO>>(models);
        public IEnumerable<TModel> ToModel(IEnumerable<TDTO> dtos) => _mapper.Map<IEnumerable<TModel>>(dtos);

        #endregion

        #region CRUD Methods

        /// <summary>Get all (non-archived).</summary>
        public virtual IEnumerable<TModel> GetAll()
        {
            if (!AllowGetAll) throw new MethodAccessException($"{nameof(GetAll)} method is not allowed.");
            try
            {
                var dtos = _repository.GetAll();
                var models = ToModel(dtos).ToList();
                return PostGetAll(models);
            }
            catch (RepositoryException) { throw; }
            catch (Exception ex)
            {
                throw new ServiceException(ex, nameof(GetAll), _serviceName);
            }
        }

        /// <summary>Get all with archived toggle (used by BaseController route /withArchived).</summary>
        public virtual IEnumerable<TModel> GetAllWithArchived(bool withArchived = true)
        {
            if (!AllowGetAllWithArchived) throw new MethodAccessException($"{nameof(GetAllWithArchived)} method is not allowed.");
            try
            {
                var dtos = _repository.GetAll(withArchived);
                var models = ToModel(dtos).ToList();
                return PostGetAll(models);
            }
            catch (RepositoryException) { throw; }
            catch (Exception ex)
            {
                throw new ServiceException(ex, nameof(GetAllWithArchived), _serviceName, withArchived);
            }
        }

        /// <summary>Get a single item by Id. If allowed and Id is empty, returns a new model instance.</summary>
        public virtual TModel GetById(Guid id)
        {
            if (!AllowGetById) throw new MethodAccessException($"{nameof(GetById)} method is not allowed.");

            if (AllowNewModel && id == Guid.Empty)
            {
                return NewModel(); // allow easy initialization (e.g., blank ProgramStage)
            }

            try
            {
                var dto = _repository.GetById(id);
                var model = ToModel(dto);
                return PostGetById(model);
            }
            catch (RepositoryException) { throw; }
            catch (Exception ex)
            {
                throw new ServiceException(ex, nameof(GetById), _serviceName, id);
            }
        }

        /// <summary>Get multiple items by Ids.</summary>
        public virtual IEnumerable<TModel> GetByIds(IEnumerable<Guid> ids)
        {
            if (!AllowGetByIds) throw new MethodAccessException($"{nameof(GetByIds)} method is not allowed.");

            try
            {
                var idList = ids?.ToList() ?? new List<Guid>();
                if (idList.Count == 0) return Enumerable.Empty<TModel>();

                var dtos = _repository.GetByIds(idList);
                var models = ToModel(dtos).ToList();
                return PostGetByIds(models);
            }
            catch (RepositoryException) { throw; }
            catch (Exception ex)
            {
                throw new ServiceException(ex, nameof(GetByIds), _serviceName, ids);
            }
        }

        /// <summary>Create or update a single item.</summary>
        public virtual TModel Save(TModel model)
        {
            if (!AllowSave) throw new MethodAccessException($"{nameof(Save)} method is not allowed.");

            bool isUpdate = model.Id != Guid.Empty;

            // Pre-save hook
            PreSave(model, isUpdate);
            isUpdate = model.Id != Guid.Empty;

            try
            {
                var dto = ToDto(model);

                // If creating, ensure new Id (works with repo rule that can also generate Ids)
                if (!isUpdate && dto.Id == Guid.Empty)
                {
                    dto.Id = Guid.NewGuid();
                }

                _repository.Save(dto);
                model.Id = dto.Id;

                // Post-save hook
                PostSave(model, isUpdate);

                // Audit
                if (isUpdate) LogUpdate(dto);
                else LogCreate(dto);

                return ToModel(dto);
            }
            catch (RepositoryException) { throw; }
            catch (Exception ex)
            {
                throw new ServiceException(ex, nameof(Save), _serviceName, model);
            }
        }

        /// <summary>Bulk save. Honors AllowSaveAll (or override to allow).</summary>
        public virtual IEnumerable<TModel> SaveAll(IEnumerable<TModel> models)
        {
            if (!AllowSaveAll) throw new MethodAccessException($"{nameof(SaveAll)} method is not allowed.");

            var list = models?.ToList() ?? new List<TModel>();
            if (list.Count == 0) return Enumerable.Empty<TModel>();

            try
            {
                // Pre hooks
                foreach (var m in list)
                {
                    var isUpdate = m.Id != Guid.Empty;
                    PreSave(m, isUpdate);
                }

                var dtos = list.Select(ToDto).ToList();

                // Ensure IDs for creates
                foreach (var dto in dtos)
                    if (dto.Id == Guid.Empty) dto.Id = Guid.NewGuid();

                var saved = _repository.SaveAll(dtos);

                // Align back ids & post hooks
                for (int i = 0; i < saved.Count; i++)
                {
                    list[i].Id = saved[i].Id;
                    var isUpdate = dtos[i].Id != Guid.Empty; // after mapping; mostly true here
                    PostSave(list[i], isUpdate);
                }

                // Audit
                foreach (var dto in saved)
                {
                    // If needed, you can add logic to separate create/update audits
                    LogUpdate(dto);
                }

                return ToModel(saved).ToList();
            }
            catch (RepositoryException) { throw; }
            catch (Exception ex)
            {
                throw new ServiceException(ex, nameof(SaveAll), _serviceName, models);
            }
        }

        /// <summary>Archive a single item.</summary>
        public virtual void Archive(TModel model)
        {
            if (!AllowArchive) throw new MethodAccessException($"{nameof(Archive)} method is not allowed.");
            ValidateModel(model);

            PreArchive(model);

            try
            {
                var dto = ToDto(model);
                _repository.Archive(dto);
                PostArchive(model);
            }
            catch (RepositoryException) { throw; }
            catch (Exception ex)
            {
                throw new ServiceException(ex, nameof(Archive), _serviceName, model);
            }
        }

        /// <summary>Archive many items.</summary>
        public virtual void ArchiveAll(IEnumerable<TModel> models)
        {
            if (!AllowArchive) throw new MethodAccessException($"{nameof(ArchiveAll)} method is not allowed.");
            var list = models?.ToList() ?? new List<TModel>();
            if (list.Count == 0) return;

            try
            {
                foreach (var m in list) PreArchive(m);

                var dtos = list.Select(ToDto).ToList();
                _repository.ArchiveAll(dtos);

                foreach (var m in list) PostArchive(m);
            }
            catch (RepositoryException) { throw; }
            catch (Exception ex)
            {
                throw new ServiceException(ex, nameof(ArchiveAll), _serviceName, models);
            }
        }

        /// <summary>Unarchive a single item.</summary>
        public virtual void Unarchive(TModel model)
        {
            if (!AllowArchive) throw new MethodAccessException($"{nameof(Unarchive)} method is not allowed.");
            ValidateModel(model);

            try
            {
                var dto = ToDto(model);
                _repository.Unarchive(dto);
            }
            catch (RepositoryException) { throw; }
            catch (Exception ex)
            {
                throw new ServiceException(ex, nameof(Unarchive), _serviceName, model);
            }
        }

        /// <summary>Unarchive many items.</summary>
        public virtual void UnarchiveAll(IEnumerable<TModel> models)
        {
            if (!AllowArchive) throw new MethodAccessException($"{nameof(UnarchiveAll)} method is not allowed.");
            var list = models?.ToList() ?? new List<TModel>();
            if (list.Count == 0) return;

            try
            {
                var dtos = list.Select(ToDto).ToList();
                _repository.UnarchiveAll(dtos);
            }
            catch (RepositoryException) { throw; }
            catch (Exception ex)
            {
                throw new ServiceException(ex, nameof(UnarchiveAll), _serviceName, models);
            }
        }

        /// <summary>Delete by Id.</summary>
        public virtual void Delete(Guid id)
        {
            if (!AllowDelete) throw new MethodAccessException($"{nameof(Delete)} method is not allowed.");

            // Load to let derived classes hook into delete lifecycle and for auditing
            var model = GetById(id);
            var dto = ToDto(model);

            PreDelete(model);

            try
            {
                _repository.DeleteById(id); // consistent with your repo method names
                PostDelete(model);
                LogDelete(dto);
            }
            catch (RepositoryException) { throw; }
            catch (Exception ex)
            {
                throw new ServiceException(ex, nameof(Delete), _serviceName, id);
            }
        }

        #endregion

        #region Helper Methods

        protected void ValidateModel(TModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model), "The model provided is null.");
        }

        #endregion

        #region Overridable Hooks for Derived Classes

        protected virtual List<TModel> PostGetAll(List<TModel> models) => models;
        protected virtual TModel PostGetById(TModel model) => model;
        protected virtual List<TModel> PostGetByIds(List<TModel> models) => models;

        protected virtual void PreSave(TModel model, bool isUpdate) { }
        protected virtual void PostSave(TModel model, bool isUpdate) { }
        protected virtual void PreDelete(TModel model) { }
        protected virtual void PostDelete(TModel model) { }
        protected virtual void PreArchive(TModel model) { }
        protected virtual void PostArchive(TModel model) { }

        #endregion

        #region Methods for Logging

        protected abstract TModel NewModel();

        protected virtual void LogCreate(TDTO dto)
        {
            if (_typedAudit != null)
                _typedAudit.LogCreate(dto);
            else
                _audit.LogAudit(
                    new AuditEvent()
                    {
                        EntityId = dto.Id,
                        EntityName = typeof(TDTO).Name,
                        Action = AuditAction.Create,
                        Data = dto
                    });
        }

        protected virtual void LogUpdate(TDTO dto)
        {
            if (_typedAudit != null)
                _typedAudit.LogUpdate(dto);
            else
                _audit.LogAudit(
                    new AuditEvent()
                    {
                        EntityId = dto.Id,
                        EntityName = typeof(TDTO).Name,
                        Action = AuditAction.Update,
                        Data = dto
                    });
        }

        protected virtual void LogDelete(TDTO dto)
        {
            if (_typedAudit != null)
                _typedAudit.LogDelete(dto);
            else
                _audit.LogAudit(
                    new AuditEvent()
                    {
                        EntityId = dto.Id,
                        EntityName = typeof(TDTO).Name,
                        Action = AuditAction.Delete,
                        Data = dto
                    }
                    );
        }

        protected virtual AuditEvent BuildAuditEvent(AuditAction action, TDTO dto, object? data = null)
        => AuditEventBuilder.Build(action, dto, data);


        #endregion
    }
}
