using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using IBeam.DataModels.System;              // IDTO
using IBeam.Repositories.Interfaces;       // IBaseRepositoryAsync<T>
using IBeam.Services;                      // IAuditService
using IBeam.Services.Abstractions;
using IBeam.Utilities;
using IBeam.Utilities.Auditing;
using IBeam.Utilities.Exceptions;                     // RepositoryException, AuditEvent, AuditAction, ServiceException

namespace IBeam.Services.Base
{
    /// <summary>
    /// Async-first base service with mapping, guardrails, and auditing.
    /// - TDTO: persistence-facing type implementing IDTO (has Guid Id)
    /// - TModel: API/domain model type
    /// </summary>
    public abstract class BaseServiceAsync<TDTO, TModel> : IBaseServiceAsync<TDTO, TModel>
        where TDTO : class, IDTO
        where TModel : class
    {
        protected readonly string _serviceName;
        protected readonly IBaseRepositoryAsync<TDTO> _repository;
        protected readonly IMapper _mapper;
        protected readonly IAuditServiceAsync _audit;
        private readonly IEntityAuditServiceAsync<TDTO>? _typedAudit;


        // Feature flags (override in derived services)
        protected virtual bool AllowGetAll { get; set; } = false;
        protected virtual bool AllowGetAllWithArchived { get; set; } = false;
        protected virtual bool AllowGetById { get; set; } = true;
        protected virtual bool AllowGetByIds { get; set; } = false;
        protected virtual bool AllowSave { get; set; } = false;
        protected virtual bool AllowSaveAll { get; set; } = false;
        protected virtual bool AllowArchive { get; set; } = false;
        protected virtual bool AllowDelete { get; set; } = false;
        protected virtual bool AllowNewModel { get; set; } = true;

        protected BaseServiceAsync(
            IBaseServicesAsync baseServices,
            IBaseRepositoryAsync<TDTO> repository)
        {
            _serviceName = GetType().Name;
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _mapper = baseServices?.Mapper ?? throw new ArgumentNullException(nameof(baseServices));
            _audit = baseServices.AuditService ?? throw new ArgumentNullException(nameof(baseServices.AuditService));
            _typedAudit = _audit as IEntityAuditServiceAsync<TDTO>;
        }

        // ---------------- Mapping ----------------
        public TDTO ToDto(TModel model) => _mapper.Map<TDTO>(model);
        public TModel ToModel(TDTO dto) => _mapper.Map<TModel>(dto);
        public IEnumerable<TDTO> ToDto(IEnumerable<TModel> models) => _mapper.Map<IEnumerable<TDTO>>(models);
        public IEnumerable<TModel> ToModel(IEnumerable<TDTO> dtos) => _mapper.Map<IEnumerable<TModel>>(dtos);

        // ---------------- Reads ----------------
        public virtual async Task<IEnumerable<TModel>> GetAllAsync(CancellationToken ct = default)
        {
            if (!AllowGetAll) throw new MethodAccessException($"{nameof(GetAllAsync)} is not allowed.");
            try
            {
                var dtos = await _repository.GetAllAsync(false, ct);
                var models = ToModel(dtos).ToList();
                return PostGetAll(models);
            }
            catch (RepositoryException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, _serviceName, nameof(GetAllAsync)); }
        }

        public virtual async Task<IEnumerable<TModel>> GetAllWithArchivedAsync(bool withArchived = true, CancellationToken ct = default)
        {
            if (!AllowGetAllWithArchived) throw new MethodAccessException($"{nameof(GetAllWithArchivedAsync)} is not allowed.");
            try
            {
                var dtos = await _repository.GetAllAsync(withArchived, ct);
                var models = ToModel(dtos).ToList();
                return PostGetAll(models);
            }
            catch (RepositoryException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, _serviceName, nameof(GetAllWithArchivedAsync), withArchived); }
        }

        public virtual async Task<TModel> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            if (!AllowGetById) throw new MethodAccessException($"{nameof(GetByIdAsync)} is not allowed.");
            if (AllowNewModel && id == Guid.Empty) return NewModel();

            try
            {
                var dto = await _repository.GetByIdAsync(id, ct);
                var model = ToModel(dto);
                return PostGetById(model);
            }
            catch (RepositoryException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, _serviceName, nameof(GetByIdAsync), id); }
        }

        public virtual async Task<IEnumerable<TModel>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
        {
            if (!AllowGetByIds) throw new MethodAccessException($"{nameof(GetByIdsAsync)} is not allowed.");
            var list = ids?.ToList() ?? new List<Guid>();
            if (list.Count == 0) return Enumerable.Empty<TModel>();

            try
            {
                var dtos = await _repository.GetByIdsAsync(list, ct);
                var models = ToModel(dtos).ToList();
                return PostGetByIds(models);
            }
            catch (RepositoryException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, _serviceName, nameof(GetByIdsAsync), ids); }
        }

        // ---------------- Writes ----------------
        public virtual async Task<TModel> SaveAsync(TModel model, CancellationToken ct = default)
        {
            if (!AllowSave) throw new MethodAccessException($"{nameof(SaveAsync)} is not allowed.");

            // Let derived classes validate/transform first
            PreSave(model, isUpdate: false);

            var dto = ToDto(model);
            var isUpdate = dto.Id != Guid.Empty;

            if (isUpdate) PreSave(model, isUpdate: true);

            try
            {
                var saved = await _repository.SaveAsync(dto, ct);
                var result = ToModel(saved);

                PostSave(result, isUpdate);
                if(isUpdate) await LogUpdateAsync(saved, result, ct);
                else await LogCreateAsync(saved, result, ct);

                return result;
            }
            catch (RepositoryException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, _serviceName, nameof(SaveAsync), model); }
        }

        public virtual async Task<IEnumerable<TModel>> SaveAllAsync(IEnumerable<TModel> models, CancellationToken ct = default)
        {
            if (!AllowSaveAll) throw new MethodAccessException($"{nameof(SaveAllAsync)} is not allowed.");
            var list = models?.ToList() ?? new List<TModel>();
            if (list.Count == 0) return Enumerable.Empty<TModel>();

            foreach (var m in list) PreSave(m, isUpdate: false);
            var dtos = list.Select(ToDto).ToList();

            try
            {
                var saved = await _repository.SaveAllAsync(dtos, ct);
                var result = ToModel(saved).ToList();

                for (int i = 0; i < result.Count; i++)
                {
                    var isUpdate = saved[i].Id != Guid.Empty;
                    PostSave(result[i], isUpdate);
                    if (isUpdate) await LogUpdateAsync(saved[i], result[i], ct);
                    else await LogCreateAsync(saved[i], result[i], ct);
                }

                return result;
            }
            catch (RepositoryException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, _serviceName, nameof(SaveAllAsync), models); }
        }

        // ---------------- Archive / Unarchive ----------------
        public virtual async Task ArchiveAsync(TModel model, CancellationToken ct = default)
        {
            if (!AllowArchive) throw new MethodAccessException($"{nameof(ArchiveAsync)} is not allowed.");
            ValidateModel(model);
            PreArchive(model);

            var dto = ToDto(model);
            try
            {
                await _repository.ArchiveAsync(dto, ct);
                PostArchive(model);
                await _audit.LogAsync(BuildAuditEvent(AuditAction.Archive, dto, model), ct);
            }
            catch (RepositoryException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, _serviceName, nameof(ArchiveAsync), model); }
        }

        public virtual async Task ArchiveAllAsync(IEnumerable<TModel> models, CancellationToken ct = default)
        {
            if (!AllowArchive) throw new MethodAccessException($"{nameof(ArchiveAllAsync)} is not allowed.");
            var list = models?.ToList() ?? new List<TModel>();
            if (list.Count == 0) return;

            foreach (var m in list) PreArchive(m);
            var dtos = list.Select(ToDto).ToList();

            try
            {
                await _repository.ArchiveAllAsync(dtos, ct);
                foreach (var dto in dtos)
                    await _audit.LogAsync(BuildAuditEvent(AuditAction.Archive, dto), ct);
            }
            catch (RepositoryException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, _serviceName, nameof(ArchiveAllAsync), models); }
        }

        public virtual async Task UnarchiveAsync(TModel model, CancellationToken ct = default)
        {
            if (!AllowArchive) throw new MethodAccessException($"{nameof(UnarchiveAsync)} is not allowed.");
            ValidateModel(model);

            var dto = ToDto(model);
            try
            {
                await _repository.UnarchiveAsync(dto, ct);
                await _audit.LogAsync(BuildAuditEvent(AuditAction.Unarchive, dto, model), ct);
            }
            catch (RepositoryException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, _serviceName, nameof(UnarchiveAsync), model); }
        }

        public virtual async Task UnarchiveAllAsync(IEnumerable<TModel> models, CancellationToken ct = default)
        {
            if (!AllowArchive) throw new MethodAccessException($"{nameof(UnarchiveAllAsync)} is not allowed.");
            var list = models?.ToList() ?? new List<TModel>();
            if (list.Count == 0) return;

            var dtos = list.Select(ToDto).ToList();
            try
            {
                await _repository.UnarchiveAllAsync(dtos, ct);
                foreach (var dto in dtos)
                    await _audit.LogAsync(BuildAuditEvent(AuditAction.Unarchive, dto), ct);
            }
            catch (RepositoryException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, _serviceName, nameof(UnarchiveAllAsync), models); }
        }

        // ---------------- Delete ----------------
        public virtual async Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            if (!AllowDelete) throw new MethodAccessException($"{nameof(DeleteAsync)} is not allowed.");

            // Load entity for auditing and hooks
            var dto = await _repository.GetByIdAsync(id, ct);
            var model = ToModel(dto);

            PreDelete(model);
            try
            {
                await _repository.DeleteByIdAsync(id, ct);
                PostDelete(model);
                await LogDeleteAsync(dto, model, ct);
            }
            catch (RepositoryException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, _serviceName, nameof(DeleteAsync), id); }
        }

        // ---------------- Hooks / Validation ----------------
        protected virtual List<TModel> PostGetAll(List<TModel> models) => models;
        protected virtual TModel PostGetById(TModel model) => model;
        protected virtual List<TModel> PostGetByIds(List<TModel> models) => models;

        protected virtual void PreSave(TModel model, bool isUpdate) { }
        protected virtual void PostSave(TModel model, bool isUpdate) { }
        protected virtual void PreDelete(TModel model) { }
        protected virtual void PostDelete(TModel model) { }
        protected virtual void PreArchive(TModel model) { }
        protected virtual void PostArchive(TModel model) { }

        protected abstract TModel NewModel();

        protected void ValidateModel(TModel model)
        {
            if (model is null)
                throw new ArgumentNullException(nameof(model), "The model provided is null.");
        }

        // ---------------- Audit helpers ----------------
        protected Task LogCreateAsync(TDTO dto, object? data, CancellationToken ct = default) =>
        _typedAudit != null
        ? _typedAudit.LogCreateAsync(dto, ct)
        : _audit.LogAsync(BuildAuditEvent(AuditAction.Create, dto, data), ct);

        protected Task LogUpdateAsync(TDTO dto, object? data, CancellationToken ct = default) =>
            _typedAudit != null
                ? _typedAudit.LogUpdateAsync(dto, ct)
                : _audit.LogAsync(BuildAuditEvent(AuditAction.Update, dto, data), ct);

        protected Task LogDeleteAsync(TDTO dto, object? data, CancellationToken ct = default) =>
            _typedAudit != null
                ? _typedAudit.LogDeleteAsync(dto, ct)
                : _audit.LogAsync(BuildAuditEvent(AuditAction.Delete, dto, data), ct);

        protected virtual AuditEvent BuildAuditEvent(AuditAction action, TDTO dto, object? data = null)
        => AuditEventBuilder.Build(action, dto, data);
    }
}
