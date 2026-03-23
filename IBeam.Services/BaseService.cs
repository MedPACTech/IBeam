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
    public abstract class BaseService<TEntity, TModel> : IBaseService<TEntity, TModel>
        where TEntity : class, IEntity
        where TModel : class
    {
        protected readonly string _serviceName;
        protected readonly IBaseRepository<TEntity> _repository;
        protected readonly IModelMapper<TEntity, TModel> _mapper;
        protected readonly IServiceOperationPolicyResolver? _policyResolver;

        protected readonly IAuditService? _audit; // optional
        protected readonly IEntityAuditService<TEntity>? _typedAudit; // optional overlay

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

        protected BaseService(
            IBaseRepository<TEntity> repository,
            IModelMapper<TEntity, TModel> mapper,
            IAuditService? audit = null,
            IServiceOperationPolicyResolver? policyResolver = null,
            IAuditTrailSink? auditTrailSink = null,
            IAuditActorProvider? auditActorProvider = null,
            IOptionsMonitor<ServiceAuditOptions>? auditOptionsMonitor = null,
            ITenantContext? tenantContext = null)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _audit = audit;
            _typedAudit = audit as IEntityAuditService<TEntity>;
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

        protected void TryWriteTransactionAudit(
            ServiceAuditOperation operation,
            Guid? entityId,
            string? originalJson,
            string? transformedJson)
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
                _auditTrailSink.WriteTransactionAsync(txn).GetAwaiter().GetResult();
            }
            catch when (!options.FailOnAuditError)
            {
                // Keep service flow resilient by default.
            }
        }

        protected void TryWriteSelectAudit(ServiceAuditOperation operation, string querySignature)
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
                _auditTrailSink.UpsertSelectRollupAsync(rollup).GetAwaiter().GetResult();
            }
            catch when (!options.FailOnAuditError)
            {
                // Keep service flow resilient by default.
            }
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
            if (!IsOperationAllowed(ServiceOperation.GetAll, AllowGetAll))
                throw new MethodAccessException($"{nameof(GetAll)} is not allowed.");

            try
            {
                var entities = _repository.GetAll();
                var models = PostGetAll(ToModel(entities).ToList());
                TryWriteSelectAudit(ServiceAuditOperation.GetAll, BuildQuerySignature("GetAll", "includeArchived=false", "includeDeleted=false"));
                return models;
            }
            catch (RepositoryException) { throw; }
            catch (RepositoryStoreException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(GetAll), _serviceName); }
        }

        public virtual IEnumerable<TModel> GetAllWithArchived(bool includeArchived = true)
        {
            if (!IsOperationAllowed(ServiceOperation.GetAllWithArchived, AllowGetAllWithArchived))
                throw new MethodAccessException($"{nameof(GetAllWithArchived)} is not allowed.");

            try
            {
                var entities = _repository.GetAll(includeArchived);
                var models = PostGetAll(ToModel(entities).ToList());
                TryWriteSelectAudit(ServiceAuditOperation.GetAllWithArchived, BuildQuerySignature("GetAllWithArchived", $"includeArchived={includeArchived}", "includeDeleted=false"));
                return models;
            }
            catch (RepositoryException) { throw; }
            catch (RepositoryStoreException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(GetAllWithArchived), _serviceName); }
        }

        public virtual TModel GetById(Guid id)
        {
            if (!IsOperationAllowed(ServiceOperation.GetById, AllowGetById))
                throw new MethodAccessException($"{nameof(GetById)} is not allowed.");

            try
            {
                var entity = _repository.GetById(id);
                if (entity is null)
                    throw new KeyNotFoundException($"{typeof(TEntity).Name} with id '{id}' was not found.");

                var model = PostGetById(ToModel(entity));
                TryWriteSelectAudit(ServiceAuditOperation.GetById, BuildQuerySignature("GetById", id.ToString("D")));
                return model;
            }
            catch (KeyNotFoundException) { throw; }
            catch (RepositoryException) { throw; }
            catch (RepositoryStoreException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(GetById), _serviceName); }
        }

        public virtual IEnumerable<TModel> GetByIds(IEnumerable<Guid> ids)
        {
            if (!IsOperationAllowed(ServiceOperation.GetByIds, AllowGetByIds))
                throw new MethodAccessException($"{nameof(GetByIds)} is not allowed.");

            var list = ids?.ToList() ?? new List<Guid>();
            if (list.Count == 0) return Enumerable.Empty<TModel>();

            try
            {
                var entities = _repository.GetByIds(list);
                var models = PostGetByIds(ToModel(entities).ToList());
                var idsSig = string.Join(",", list.OrderBy(x => x).Select(x => x.ToString("N")));
                TryWriteSelectAudit(ServiceAuditOperation.GetByIds, BuildQuerySignature("GetByIds", idsSig));
                return models;
            }
            catch (RepositoryException) { throw; }
            catch (RepositoryStoreException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(GetByIds), _serviceName); }
        }

        // ---- Writes ----
        public virtual TModel Save(TModel model)
        {
            if (!IsOperationAllowed(ServiceOperation.Save, AllowSave))
                throw new MethodAccessException($"{nameof(Save)} is not allowed.");

            try
            {
                var entityCandidate = ToEntity(model);
                var isUpdate = entityCandidate.Id != Guid.Empty;
                var originalJson = isUpdate
                    ? _repository.GetById(entityCandidate.Id, includeArchived: true, includeDeleted: true) is TEntity existing
                        ? SerializeEntity(existing)
                        : null
                    : null;

                PreSave(model, isUpdate);

                var entity = ToEntity(model); // remap after PreSave in case model changed
                var saved = _repository.Save(entity);

                PostSave(model, isUpdate);

                if (isUpdate) _typedAudit?.LogUpdate(saved);
                else _typedAudit?.LogCreate(saved);

                TryWriteTransactionAudit(
                    isUpdate ? ServiceAuditOperation.Update : ServiceAuditOperation.Create,
                    saved.Id,
                    originalJson,
                    SerializeEntity(saved));

                return ToModel(saved);
            }
            catch (RepositoryException) { throw; }
            catch (RepositoryStoreException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(Save), _serviceName); }
        }

        public virtual IEnumerable<TModel> SaveAll(IEnumerable<TModel> models)
        {
            if (!IsOperationAllowed(ServiceOperation.SaveAll, AllowSaveAll))
                throw new MethodAccessException($"{nameof(SaveAll)} is not allowed.");

            var list = models?.ToList() ?? new List<TModel>();
            if (list.Count == 0) return Enumerable.Empty<TModel>();

            try
            {
                var isUpdates = new List<bool>(list.Count);
                var originalById = new Dictionary<Guid, string>();
                foreach (var model in list)
                {
                    var candidate = ToEntity(model);
                    var isUpdate = candidate.Id != Guid.Empty;
                    isUpdates.Add(isUpdate);

                    if (isUpdate)
                    {
                        var existing = _repository.GetById(candidate.Id, includeArchived: true, includeDeleted: true);
                        if (existing is not null)
                        {
                            originalById[candidate.Id] = SerializeEntity(existing);
                        }
                    }

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
                    var isUpdate = isUpdates[i];
                    if (isUpdate) _typedAudit?.LogUpdate(saved[i]);
                    else _typedAudit?.LogCreate(saved[i]);

                    var originalJson = isUpdate && originalById.TryGetValue(saved[i].Id, out var original) ? original : null;
                    TryWriteTransactionAudit(
                        isUpdate ? ServiceAuditOperation.Update : ServiceAuditOperation.Create,
                        saved[i].Id,
                        originalJson,
                        SerializeEntity(saved[i]));
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
            if (!IsOperationAllowed(ServiceOperation.Archive, AllowArchive))
                throw new MethodAccessException($"{nameof(Archive)} is not allowed.");
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
            if (!IsOperationAllowed(ServiceOperation.Archive, AllowArchive))
                throw new MethodAccessException($"{nameof(ArchiveAll)} is not allowed.");

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
            if (!IsOperationAllowed(ServiceOperation.Unarchive, AllowUnarchive))
                throw new MethodAccessException($"{nameof(Unarchive)} is not allowed.");
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
            if (!IsOperationAllowed(ServiceOperation.Unarchive, AllowUnarchive))
                throw new MethodAccessException($"{nameof(UnarchiveAll)} is not allowed.");

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
            if (!IsOperationAllowed(ServiceOperation.Delete, AllowDelete))
                throw new MethodAccessException($"{nameof(Delete)} is not allowed.");
            try
            {
                PreDelete(id);
                var existing = _repository.GetById(id, includeArchived: true, includeDeleted: true);
                var originalJson = existing is not null ? SerializeEntity(existing) : null;

                _repository.Delete(id);

                PostDelete(id);
                if (existing is not null)
                {
                    _typedAudit?.LogDelete(existing);
                }

                TryWriteTransactionAudit(ServiceAuditOperation.Delete, id, originalJson, null);
            }
            catch (RepositoryException) { throw; }
            catch (RepositoryStoreException) { throw; }
            catch (Exception ex) { throw new ServiceException(ex, nameof(Delete), _serviceName); }
        }
    }
}
