using IBeam.Repositories.Abstractions;
using IBeam.Repositories.Core;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        protected readonly IServiceOperationPolicyResolver? _policyResolver;

        protected readonly IAuditServiceAsync? _audit;
        protected readonly IEntityAuditServiceAsync<TEntity>? _typedAudit;

        protected readonly IAuditTrailSink _auditTrailSink;
        protected readonly IAuditActorProvider _auditActorProvider;
        protected readonly IOptionsMonitor<ServiceAuditOptions>? _auditOptionsMonitor;
        protected readonly ITenantContext? _tenantContext;

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
            IAuditServiceAsync? audit = null,
            IServiceOperationPolicyResolver? policyResolver = null,
            IAuditTrailSink? auditTrailSink = null,
            IAuditActorProvider? auditActorProvider = null,
            IOptionsMonitor<ServiceAuditOptions>? auditOptionsMonitor = null,
            ITenantContext? tenantContext = null)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _audit = audit;
            _typedAudit = audit as IEntityAuditServiceAsync<TEntity>;
            _policyResolver = policyResolver;
            _auditTrailSink = auditTrailSink ?? new NoOpAuditTrailSink();
            _auditActorProvider = auditActorProvider ?? new NoOpAuditActorProvider();
            _auditOptionsMonitor = auditOptionsMonitor;
            _tenantContext = tenantContext;
            _serviceName = GetType().Name;
        }

        protected bool IsOperationAllowed(ServiceOperation operation, bool fallback)
            => _policyResolver?.IsAllowed(GetType(), operation, fallback) ?? fallback;

        protected ServiceAuditOptions CurrentAuditOptions
            => _auditOptionsMonitor?.CurrentValue ?? new ServiceAuditOptions();

        protected Guid? CurrentTenantId() => _tenantContext?.TenantId;

        protected string SerializeEntity(TEntity entity)
            => JsonSerializer.Serialize(entity);

        protected string BuildQuerySignature(string name, params string[] values)
        {
            var raw = string.Join("|", values);
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
            var hash = Convert.ToHexString(bytes);
            return $"{name}:{hash}";
        }

        protected async Task TryWriteTransactionAuditAsync(
            ServiceAuditOperation operation,
            Guid? entityId,
            string? originalJson,
            string? transformedJson,
            CancellationToken ct)
        {
            var options = CurrentAuditOptions;
            if (!options.Enabled)
            {
                return;
            }

            var txn = new ServiceAuditTransaction
            {
                ServiceName = _serviceName,
                EntityName = typeof(TEntity).Name,
                Operation = operation,
                EntityId = entityId,
                TenantId = CurrentTenantId(),
                ActorId = _auditActorProvider.GetActorId(),
                CorrelationId = null,
                OriginalJson = originalJson,
                TransformedJson = transformedJson,
                OccurredUtc = DateTimeOffset.UtcNow
            };

            try
            {
                await _auditTrailSink.WriteTransactionAsync(txn, ct).ConfigureAwait(false);
            }
            catch when (!options.FailOnAuditError)
            {
                // Keep service flow resilient by default.
            }
        }

        protected async Task TryWriteSelectAuditAsync(ServiceAuditOperation operation, string querySignature, CancellationToken ct)
        {
            var options = CurrentAuditOptions;
            if (!options.Enabled || !options.EnableSelectAudits || options.SelectMode == SelectAuditMode.None)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var rollup = new ServiceSelectAuditRollup
            {
                DateUtc = DateOnly.FromDateTime(now.UtcDateTime),
                ServiceName = _serviceName,
                EntityName = typeof(TEntity).Name,
                Operation = operation,
                TenantId = CurrentTenantId(),
                ActorId = _auditActorProvider.GetActorId(),
                QuerySignature = querySignature,
                FirstSeenUtc = now,
                LastSeenUtc = now,
                Count = 1
            };

            try
            {
                await _auditTrailSink.UpsertSelectRollupAsync(rollup, ct).ConfigureAwait(false);
            }
            catch when (!options.FailOnAuditError)
            {
                // Keep service flow resilient by default.
            }
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
            if (!IsOperationAllowed(ServiceOperation.GetAll, AllowGetAll))
                throw new MethodAccessException($"{nameof(GetAllAsync)} is not allowed.");

            try
            {
                var entities = await _repository.GetAllAsync(ct: ct).ConfigureAwait(false);
                var models = PostGetAll(ToModel(entities).ToList());
                await TryWriteSelectAuditAsync(ServiceAuditOperation.GetAll, BuildQuerySignature("GetAll", "includeArchived=false", "includeDeleted=false"), ct).ConfigureAwait(false);
                return models;
            }
            catch (RepositoryException) { throw; }
            catch (RepositoryStoreException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(GetAllAsync), _serviceName); }
        }

        public virtual async Task<IEnumerable<TModel>> GetAllWithArchivedAsync(bool includeArchived = true, CancellationToken ct = default)
        {
            if (!IsOperationAllowed(ServiceOperation.GetAllWithArchived, AllowGetAllWithArchived))
                throw new MethodAccessException($"{nameof(GetAllWithArchivedAsync)} is not allowed.");

            try
            {
                var entities = await _repository.GetAllAsync(includeArchived, ct: ct).ConfigureAwait(false);
                var models = PostGetAll(ToModel(entities).ToList());
                await TryWriteSelectAuditAsync(ServiceAuditOperation.GetAllWithArchived, BuildQuerySignature("GetAllWithArchived", $"includeArchived={includeArchived}", "includeDeleted=false"), ct).ConfigureAwait(false);
                return models;
            }
            catch (RepositoryException) { throw; }
            catch (RepositoryStoreException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(GetAllWithArchivedAsync), _serviceName); }
        }

        public virtual async Task<TModel> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            if (!IsOperationAllowed(ServiceOperation.GetById, AllowGetById))
                throw new MethodAccessException($"{nameof(GetByIdAsync)} is not allowed.");

            try
            {
                var entity = await _repository.GetByIdAsync(id, ct: ct).ConfigureAwait(false);
                if (entity is null)
                    throw new KeyNotFoundException($"{typeof(TEntity).Name} with id '{id}' was not found.");

                var model = PostGetById(ToModel(entity));
                await TryWriteSelectAuditAsync(ServiceAuditOperation.GetById, BuildQuerySignature("GetById", id.ToString("D")), ct).ConfigureAwait(false);
                return model;
            }
            catch (KeyNotFoundException) { throw; }
            catch (RepositoryException) { throw; }
            catch (RepositoryStoreException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(GetByIdAsync), _serviceName); }
        }

        public virtual async Task<IEnumerable<TModel>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
        {
            if (!IsOperationAllowed(ServiceOperation.GetByIds, AllowGetByIds))
                throw new MethodAccessException($"{nameof(GetByIdsAsync)} is not allowed.");

            var list = ids?.ToList() ?? new List<Guid>();
            if (list.Count == 0) return Enumerable.Empty<TModel>();

            try
            {
                var entities = await _repository.GetByIdsAsync(list, ct: ct).ConfigureAwait(false);
                var models = PostGetByIds(ToModel(entities).ToList());
                var idsSig = string.Join(",", list.OrderBy(x => x).Select(x => x.ToString("N")));
                await TryWriteSelectAuditAsync(ServiceAuditOperation.GetByIds, BuildQuerySignature("GetByIds", idsSig), ct).ConfigureAwait(false);
                return models;
            }
            catch (RepositoryException) { throw; }
            catch (RepositoryStoreException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(GetByIdsAsync), _serviceName); }
        }

        public virtual async Task<TModel> SaveAsync(TModel model, CancellationToken ct = default)
        {
            if (!IsOperationAllowed(ServiceOperation.Save, AllowSave))
                throw new MethodAccessException($"{nameof(SaveAsync)} is not allowed.");

            try
            {
                var entityCandidate = ToEntity(model);
                var isUpdate = entityCandidate.Id != Guid.Empty;
                string? originalJson = null;

                if (isUpdate)
                {
                    var existing = await _repository.GetByIdAsync(entityCandidate.Id, includeArchived: true, includeDeleted: true, ct).ConfigureAwait(false);
                    if (existing is not null)
                    {
                        originalJson = SerializeEntity(existing);
                    }
                }

                await PreSaveAsync(model, isUpdate, ct).ConfigureAwait(false);

                var entity = ToEntity(model);
                var saved = await _repository.SaveAsync(entity, ct).ConfigureAwait(false);

                await PostSaveAsync(model, isUpdate, ct).ConfigureAwait(false);

                if (_typedAudit != null)
                {
                    if (isUpdate) await _typedAudit.LogUpdateAsync(saved, ct).ConfigureAwait(false);
                    else await _typedAudit.LogCreateAsync(saved, ct).ConfigureAwait(false);
                }

                await TryWriteTransactionAuditAsync(
                    isUpdate ? ServiceAuditOperation.Update : ServiceAuditOperation.Create,
                    saved.Id,
                    originalJson,
                    SerializeEntity(saved),
                    ct).ConfigureAwait(false);

                return ToModel(saved);
            }
            catch (RepositoryException) { throw; }
            catch (RepositoryStoreException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(SaveAsync), _serviceName); }
        }

        public virtual async Task<IEnumerable<TModel>> SaveAllAsync(IEnumerable<TModel> models, CancellationToken ct = default)
        {
            if (!IsOperationAllowed(ServiceOperation.SaveAll, AllowSaveAll))
                throw new MethodAccessException($"{nameof(SaveAllAsync)} is not allowed.");

            var list = models?.ToList() ?? new List<TModel>();
            if (list.Count == 0) return Enumerable.Empty<TModel>();

            try
            {
                var isUpdates = new List<bool>(list.Count);
                var updateIds = new List<Guid>();
                foreach (var model in list)
                {
                    var candidate = ToEntity(model);
                    var isUpdate = candidate.Id != Guid.Empty;
                    isUpdates.Add(isUpdate);
                    if (isUpdate)
                    {
                        updateIds.Add(candidate.Id);
                    }

                    await PreSaveAsync(model, isUpdate, ct).ConfigureAwait(false);
                }

                var originalById = new Dictionary<Guid, string>();
                if (updateIds.Count > 0)
                {
                    var existing = await _repository.GetByIdsAsync(updateIds.Distinct().ToList(), includeArchived: true, includeDeleted: true, ct).ConfigureAwait(false);
                    foreach (var item in existing)
                    {
                        originalById[item.Id] = SerializeEntity(item);
                    }
                }

                var entities = ToEntity(list).ToList();
                var saved = (await _repository.SaveAllAsync(entities, ct).ConfigureAwait(false)).ToList();

                for (var i = 0; i < list.Count; i++)
                {
                    await PostSaveAsync(list[i], isUpdates[i], ct).ConfigureAwait(false);
                }

                if (_typedAudit is not null)
                {
                    for (var i = 0; i < saved.Count && i < isUpdates.Count; i++)
                    {
                        if (isUpdates[i]) await _typedAudit.LogUpdateAsync(saved[i], ct).ConfigureAwait(false);
                        else await _typedAudit.LogCreateAsync(saved[i], ct).ConfigureAwait(false);
                    }
                }

                for (var i = 0; i < saved.Count && i < isUpdates.Count; i++)
                {
                    var isUpdate = isUpdates[i];
                    var originalJson = isUpdate && originalById.TryGetValue(saved[i].Id, out var original) ? original : null;
                    await TryWriteTransactionAuditAsync(
                        isUpdate ? ServiceAuditOperation.Update : ServiceAuditOperation.Create,
                        saved[i].Id,
                        originalJson,
                        SerializeEntity(saved[i]),
                        ct).ConfigureAwait(false);
                }

                return ToModel(saved).ToList();
            }
            catch (RepositoryException) { throw; }
            catch (RepositoryStoreException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(SaveAllAsync), _serviceName); }
        }

        public virtual async Task ArchiveAsync(Guid id, CancellationToken ct = default)
        {
            if (!IsOperationAllowed(ServiceOperation.Archive, AllowArchive))
                throw new MethodAccessException($"{nameof(ArchiveAsync)} is not allowed.");

            try
            {
                await PreArchiveAsync(id, ct).ConfigureAwait(false);
                await _repository.ArchiveAsync(id, ct).ConfigureAwait(false);
                await PostArchiveAsync(id, ct).ConfigureAwait(false);
            }
            catch (RepositoryException) { throw; }
            catch (RepositoryStoreException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(ArchiveAsync), _serviceName); }
        }

        public Task ArchiveAsync(TModel model, CancellationToken ct = default)
            => ArchiveAsync(ToEntity(model).Id, ct);

        public async Task ArchiveAllAsync(IEnumerable<TModel> models, CancellationToken ct = default)
        {
            if (!IsOperationAllowed(ServiceOperation.Archive, AllowArchive))
                throw new MethodAccessException($"{nameof(ArchiveAllAsync)} is not allowed.");

            var ids = ToEntity(models ?? Array.Empty<TModel>())
                .Select(e => e.Id)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
            if (ids.Count == 0) return;

            try
            {
                foreach (var id in ids)
                    await PreArchiveAsync(id, ct).ConfigureAwait(false);

                await _repository.ArchiveAllAsync(ids, ct).ConfigureAwait(false);

                foreach (var id in ids)
                    await PostArchiveAsync(id, ct).ConfigureAwait(false);
            }
            catch (RepositoryException) { throw; }
            catch (RepositoryStoreException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(ArchiveAllAsync), _serviceName); }
        }

        public virtual async Task UnarchiveAsync(Guid id, CancellationToken ct = default)
        {
            if (!IsOperationAllowed(ServiceOperation.Unarchive, AllowUnarchive))
                throw new MethodAccessException($"{nameof(UnarchiveAsync)} is not allowed.");

            try
            {
                await PreUnarchiveAsync(id, ct).ConfigureAwait(false);
                await _repository.UnarchiveAsync(id, ct).ConfigureAwait(false);
                await PostUnarchiveAsync(id, ct).ConfigureAwait(false);
            }
            catch (RepositoryException) { throw; }
            catch (RepositoryStoreException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(UnarchiveAsync), _serviceName); }
        }

        public Task UnarchiveAsync(TModel model, CancellationToken ct = default)
            => UnarchiveAsync(ToEntity(model).Id, ct);

        public async Task UnarchiveAllAsync(IEnumerable<TModel> models, CancellationToken ct = default)
        {
            if (!IsOperationAllowed(ServiceOperation.Unarchive, AllowUnarchive))
                throw new MethodAccessException($"{nameof(UnarchiveAllAsync)} is not allowed.");

            var ids = ToEntity(models ?? Array.Empty<TModel>())
                .Select(e => e.Id)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
            if (ids.Count == 0) return;

            try
            {
                foreach (var id in ids)
                    await PreUnarchiveAsync(id, ct).ConfigureAwait(false);

                await _repository.UnarchiveAllAsync(ids, ct).ConfigureAwait(false);

                foreach (var id in ids)
                    await PostUnarchiveAsync(id, ct).ConfigureAwait(false);
            }
            catch (RepositoryException) { throw; }
            catch (RepositoryStoreException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(UnarchiveAllAsync), _serviceName); }
        }

        public virtual async Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            if (!IsOperationAllowed(ServiceOperation.Delete, AllowDelete))
                throw new MethodAccessException($"{nameof(DeleteAsync)} is not allowed.");

            try
            {
                await PreDeleteAsync(id, ct).ConfigureAwait(false);
                var existing = await _repository.GetByIdAsync(id, includeArchived: true, includeDeleted: true, ct).ConfigureAwait(false);
                var originalJson = existing is not null ? SerializeEntity(existing) : null;

                await _repository.DeleteAsync(id, ct).ConfigureAwait(false);

                await PostDeleteAsync(id, ct).ConfigureAwait(false);
                if (existing is not null && _typedAudit is not null)
                {
                    await _typedAudit.LogDeleteAsync(existing, ct).ConfigureAwait(false);
                }

                await TryWriteTransactionAuditAsync(ServiceAuditOperation.Delete, id, originalJson, null, ct).ConfigureAwait(false);
            }
            catch (RepositoryException) { throw; }
            catch (RepositoryStoreException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(DeleteAsync), _serviceName); }
        }
    }
}
