using IBeam.Repositories.Abstractions;
using IBeam.Services.Abstractions;
using IBeam.Utilities.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ServiceException = IBeam.Services.Abstractions.ServiceException;

namespace IBeam.Services.Core
{
    public abstract class BaseServiceAsync<TEntity, TModel> : IBaseServiceAsync<TEntity, TModel>
        where TEntity : class, IEntity
        where TModel : class
    {
        protected readonly string _serviceName;
        protected readonly IBaseRepositoryAsync<TEntity> _repository;
        protected readonly IModelMapper<TEntity, TModel> _mapper;

        protected readonly IAuditServiceAsync? _audit;
        protected readonly IEntityAuditServiceAsync<TEntity>? _typedAudit;

        protected virtual bool AllowGetById { get; set; } = true;
        protected virtual bool AllowGetByIds { get; set; } = false;
        protected virtual bool AllowGetAll { get; set; } = false;
        protected virtual bool AllowGetAllWithArchived { get; set; } = false;

        protected virtual bool AllowSave { get; set; } = false;
        protected virtual bool AllowSaveAll { get; set; } = false;

        protected virtual bool AllowArchive { get; set; } = false;
        protected virtual bool AllowUnarchive { get; set; } = false;
        protected virtual bool AllowDelete { get; set; } = false;

        protected BaseServiceAsync(
            IBaseRepositoryAsync<TEntity> repository,
            IModelMapper<TEntity, TModel> mapper,
            IAuditServiceAsync? audit = null)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _audit = audit;
            _typedAudit = audit as IEntityAuditServiceAsync<TEntity>;
            _serviceName = GetType().Name;
        }

        public TEntity ToEntity(TModel model) => _mapper.ToEntity(model);
        public TModel ToModel(TEntity entity) => _mapper.ToModel(entity);
        public IEnumerable<TEntity> ToEntity(IEnumerable<TModel> models) => _mapper.ToEntity(models);
        public IEnumerable<TModel> ToModel(IEnumerable<TEntity> entities) => _mapper.ToModel(entities);

        protected virtual IEnumerable<TModel> PostGetAll(IEnumerable<TModel> models) => models;
        protected virtual IEnumerable<TModel> PostGetByIds(IEnumerable<TModel> models) => models;
        protected virtual TModel PostGetById(TModel model) => model;

        protected virtual Task PreSaveAsync(TModel model, bool isUpdate, CancellationToken ct) => Task.CompletedTask;
        protected virtual Task PostSaveAsync(TModel model, bool isUpdate, CancellationToken ct) => Task.CompletedTask;

        protected virtual Task PreArchiveAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
        protected virtual Task PostArchiveAsync(Guid id, CancellationToken ct) => Task.CompletedTask;

        protected virtual Task PreUnarchiveAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
        protected virtual Task PostUnarchiveAsync(Guid id, CancellationToken ct) => Task.CompletedTask;

        protected virtual Task PreDeleteAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
        protected virtual Task PostDeleteAsync(Guid id, CancellationToken ct) => Task.CompletedTask;

        public virtual async Task<IEnumerable<TModel>> GetAllAsync(CancellationToken ct = default)
        {
            if (!AllowGetAll) throw new MethodAccessException($"{nameof(GetAllAsync)} is not allowed.");

            try
            {
                var entities = await _repository.GetAllAsync(ct: ct).ConfigureAwait(false);
                return PostGetAll(ToModel(entities).ToList());
            }
            catch (RepositoryException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(GetAllAsync), _serviceName); }
        }

        public virtual async Task<IEnumerable<TModel>> GetAllWithArchivedAsync(bool includeArchived = true, CancellationToken ct = default)
        {
            if (!AllowGetAllWithArchived) throw new MethodAccessException($"{nameof(GetAllWithArchivedAsync)} is not allowed.");

            try
            {
                var entities = await _repository.GetAllAsync(includeArchived, ct: ct).ConfigureAwait(false);
                return PostGetAll(ToModel(entities).ToList());
            }
            catch (RepositoryException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(GetAllWithArchivedAsync), _serviceName); }
        }

        public virtual async Task<TModel> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            if (!AllowGetById) throw new MethodAccessException($"{nameof(GetByIdAsync)} is not allowed.");

            try
            {
                var entity = await _repository.GetByIdAsync(id, ct: ct).ConfigureAwait(false);
                return PostGetById(ToModel(entity));
            }
            catch (RepositoryException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(GetByIdAsync), _serviceName); }
        }

        public virtual async Task<IEnumerable<TModel>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
        {
            if (!AllowGetByIds) throw new MethodAccessException($"{nameof(GetByIdsAsync)} is not allowed.");

            var list = ids?.ToList() ?? new List<Guid>();
            if (list.Count == 0) return Enumerable.Empty<TModel>();

            try
            {
                var entities = await _repository.GetByIdsAsync(list, ct: ct).ConfigureAwait(false);
                return PostGetByIds(ToModel(entities).ToList());
            }
            catch (RepositoryException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(GetByIdsAsync), _serviceName); }
        }

        public virtual async Task<TModel> SaveAsync(TModel model, CancellationToken ct = default)
        {
            if (!AllowSave) throw new MethodAccessException($"{nameof(SaveAsync)} is not allowed.");

            try
            {
                var entityCandidate = ToEntity(model);
                var isUpdate = entityCandidate.Id != Guid.Empty;

                await PreSaveAsync(model, isUpdate, ct).ConfigureAwait(false);

                var entity = ToEntity(model);
                var saved = await _repository.SaveAsync(entity, ct).ConfigureAwait(false);

                await PostSaveAsync(model, isUpdate, ct).ConfigureAwait(false);

                if (_typedAudit != null)
                {
                    if (isUpdate) await _typedAudit.LogUpdateAsync(saved, ct).ConfigureAwait(false);
                    else await _typedAudit.LogCreateAsync(saved, ct).ConfigureAwait(false);
                }

                return ToModel(saved);
            }
            catch (RepositoryException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(SaveAsync), _serviceName); }
        }

        public virtual async Task<IEnumerable<TModel>> SaveAllAsync(IEnumerable<TModel> models, CancellationToken ct = default)
        {
            if (!AllowSaveAll) throw new MethodAccessException($"{nameof(SaveAllAsync)} is not allowed.");

            var list = models?.ToList() ?? new List<TModel>();
            if (list.Count == 0) return Enumerable.Empty<TModel>();

            try
            {
                foreach (var m in list)
                {
                    var isUpdate = ToEntity(m).Id != Guid.Empty;
                    await PreSaveAsync(m, isUpdate, ct).ConfigureAwait(false);
                }

                var entities = ToEntity(list).ToList();
                var saved = (await _repository.SaveAllAsync(entities, ct).ConfigureAwait(false)).ToList();

                return ToModel(saved).ToList();
            }
            catch (RepositoryException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(SaveAllAsync), _serviceName); }
        }

        public virtual async Task ArchiveAsync(Guid id, CancellationToken ct = default)
        {
            if (!AllowArchive) throw new MethodAccessException($"{nameof(ArchiveAsync)} is not allowed.");

            try
            {
                await PreArchiveAsync(id, ct).ConfigureAwait(false);
                await _repository.ArchiveAsync(id, ct).ConfigureAwait(false);
                await PostArchiveAsync(id, ct).ConfigureAwait(false);
            }
            catch (RepositoryException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(ArchiveAsync), _serviceName); }
        }

        public Task ArchiveAsync(TModel model, CancellationToken ct = default)
            => ArchiveAsync(ToEntity(model).Id, ct);

        public async Task ArchiveAllAsync(IEnumerable<TModel> models, CancellationToken ct = default)
        {
            foreach (var id in ToEntity(models ?? Array.Empty<TModel>()).Select(e => e.Id))
                await ArchiveAsync(id, ct).ConfigureAwait(false);
        }

        public virtual async Task UnarchiveAsync(Guid id, CancellationToken ct = default)
        {
            if (!AllowUnarchive) throw new MethodAccessException($"{nameof(UnarchiveAsync)} is not allowed.");

            try
            {
                await PreUnarchiveAsync(id, ct).ConfigureAwait(false);
                await _repository.UnarchiveAsync(id, ct).ConfigureAwait(false);
                await PostUnarchiveAsync(id, ct).ConfigureAwait(false);
            }
            catch (RepositoryException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(UnarchiveAsync), _serviceName); }
        }

        public Task UnarchiveAsync(TModel model, CancellationToken ct = default)
            => UnarchiveAsync(ToEntity(model).Id, ct);

        public async Task UnarchiveAllAsync(IEnumerable<TModel> models, CancellationToken ct = default)
        {
            foreach (var id in ToEntity(models ?? Array.Empty<TModel>()).Select(e => e.Id))
                await UnarchiveAsync(id, ct).ConfigureAwait(false);
        }

        public virtual async Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            if (!AllowDelete) throw new MethodAccessException($"{nameof(DeleteAsync)} is not allowed.");

            try
            {
                await PreDeleteAsync(id, ct).ConfigureAwait(false);
                await _repository.DeleteAsync(id, ct).ConfigureAwait(false);
                await PostDeleteAsync(id, ct).ConfigureAwait(false);
            }
            catch (RepositoryException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(DeleteAsync), _serviceName); }
        }
    }
}
