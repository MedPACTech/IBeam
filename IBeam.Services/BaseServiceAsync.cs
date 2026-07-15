using IBeam.Repositories.Abstractions;
using IBeam.Repositories.Core;
using IBeam.AccessControl;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Claims;
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
        protected readonly IAuditRequestContextProvider _auditRequestContextProvider;
        protected readonly IServiceOperationAuthorizer? _serviceOperationAuthorizer;
        protected readonly IServiceOperationPrincipalProvider _serviceOperationPrincipalProvider;
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
            ITenantContext? tenantContext = null,
            IAuditRequestContextProvider? auditRequestContextProvider = null,
            IServiceOperationAuthorizer? serviceOperationAuthorizer = null,
            IServiceOperationPrincipalProvider? serviceOperationPrincipalProvider = null)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _audit = audit;
            _typedAudit = audit as IEntityAuditServiceAsync<TEntity>;
            _policyResolver = policyResolver;
            _auditTrailSink = auditTrailSink ?? new NoOpAuditTrailSink();
            _auditActorProvider = auditActorProvider ?? new NoOpAuditActorProvider();
            _auditRequestContextProvider = auditRequestContextProvider ?? new NoOpAuditRequestContextProvider();
            _serviceOperationAuthorizer = serviceOperationAuthorizer;
            _serviceOperationPrincipalProvider = serviceOperationPrincipalProvider ?? new NoOpServiceOperationPrincipalProvider();
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

        protected virtual Task DemandServiceOperationAccessAsync(ServiceAuditOperation operation, CancellationToken ct)
            => DemandServiceOperationAccessAsync(ResolveAuditAction(operation, CurrentAuditOptions), ct);

        protected virtual async Task DemandServiceOperationAccessAsync(string operationName, CancellationToken ct)
        {
            if (_serviceOperationAuthorizer is null)
            {
                return;
            }

            var tenantId = CurrentTenantId();
            if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
            {
                throw new AccessControlException("tenantId is required for service operation authorization.");
            }

            var principal = _serviceOperationPrincipalProvider.GetPrincipal()
                ?? new ClaimsPrincipal(new ClaimsIdentity());

            var result = await _serviceOperationAuthorizer.AuthorizeAsync(
                new ServiceOperationAuthorizationRequest(tenantId.Value, principal, operationName),
                ct).ConfigureAwait(false);

            if (!result.Allowed)
            {
                throw new UnauthorizedAccessException($"Access denied for service operation '{operationName}'.");
            }
        }

        protected virtual string ResolveAuditEntityName(ServiceAuditOptions options)
        {
            var serviceOptions = ResolveAuditServiceOptions(options);
            if (!string.IsNullOrWhiteSpace(serviceOptions?.EntityName))
            {
                return serviceOptions.EntityName.Trim();
            }

            var operation = GetType()
                .GetCustomAttributes(typeof(IBeamOperationAttribute), inherit: true)
                .OfType<IBeamOperationAttribute>()
                .LastOrDefault();

            if (!string.IsNullOrWhiteSpace(operation?.Name) && !operation.Name.Contains('.'))
            {
                return operation.Name.Trim();
            }

            return NormalizeEntityName(typeof(TEntity).Name);
        }

        protected virtual string ResolveAuditAction(ServiceAuditOperation operation, ServiceAuditOptions options)
        {
            var operationOptions = ResolveAuditOperationOptions(operation, options);
            if (!string.IsNullOrWhiteSpace(operationOptions?.Action))
            {
                return operationOptions.Action.Trim();
            }

            var auditAction = GetType()
                .GetCustomAttributes(typeof(IBeamAuditActionAttribute), inherit: true)
                .OfType<IBeamAuditActionAttribute>()
                .LastOrDefault();

            if (auditAction is { Enabled: true } && !string.IsNullOrWhiteSpace(auditAction.Action))
            {
                return auditAction.Action.Trim();
            }

            var operationAttribute = GetType()
                .GetCustomAttributes(typeof(IBeamOperationAttribute), inherit: true)
                .OfType<IBeamOperationAttribute>()
                .LastOrDefault();

            if (operationAttribute is { Audit: true })
            {
                if (!string.IsNullOrWhiteSpace(operationAttribute.AuditAction))
                {
                    return operationAttribute.AuditAction.Trim();
                }

                if (!string.IsNullOrWhiteSpace(operationAttribute.Name) && operationAttribute.Name.Contains('.'))
                {
                    return operationAttribute.Name.Trim();
                }
            }

            return $"{ResolveAuditEntityName(options)}.{AuditOperationSegment(operation)}";
        }

        protected bool ShouldWriteTransactionAudit(ServiceAuditOperation operation, ServiceAuditOptions options)
        {
            if (!options.Enabled)
            {
                return false;
            }

            var serviceOptions = ResolveAuditServiceOptions(options);
            if (serviceOptions?.Enabled == false)
            {
                return false;
            }

            var operationOptions = ResolveAuditOperationOptions(operation, options);
            if (operationOptions?.Enabled is bool operationEnabled)
            {
                return operationEnabled;
            }

            if (serviceOptions?.Enabled == true)
            {
                return true;
            }

            return options.DefaultMode == ServiceAuditDefaultMode.AuditWrites;
        }

        protected ServiceAuditServiceOptions? ResolveAuditServiceOptions(ServiceAuditOptions options)
        {
            if (options.Services.TryGetValue(GetType().FullName ?? string.Empty, out var full))
            {
                return full;
            }

            return options.Services.TryGetValue(GetType().Name, out var shortName) ? shortName : null;
        }

        protected ServiceAuditOperationOptions? ResolveAuditOperationOptions(ServiceAuditOperation operation, ServiceAuditOptions options)
        {
            var serviceOptions = ResolveAuditServiceOptions(options);
            if (serviceOptions is null)
            {
                return null;
            }

            if (serviceOptions.Operations.TryGetValue(operation.ToString(), out var exact))
            {
                return exact;
            }

            var alias = operation switch
            {
                ServiceAuditOperation.Create => nameof(ServiceOperation.Save),
                ServiceAuditOperation.Update => nameof(ServiceOperation.Save),
                _ => operation.ToString()
            };

            return serviceOptions.Operations.TryGetValue(alias, out var aliased) ? aliased : null;
        }

        protected bool CaptureBefore(ServiceAuditOperation operation, ServiceAuditOptions options)
            => ResolveAuditOperationOptions(operation, options)?.CaptureBefore ?? options.CaptureBefore;

        protected bool CaptureAfter(ServiceAuditOperation operation, ServiceAuditOptions options)
            => ResolveAuditOperationOptions(operation, options)?.CaptureAfter ?? options.CaptureAfter;

        private static string NormalizeEntityName(string name)
        {
            var value = name.EndsWith("Entity", StringComparison.OrdinalIgnoreCase)
                ? name[..^"Entity".Length]
                : name;

            return value.Trim().ToLowerInvariant();
        }

        private static string AuditOperationSegment(ServiceAuditOperation operation)
            => operation.ToString().ToLowerInvariant();

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
            if (!ShouldWriteTransactionAudit(operation, options))
            {
                return;
            }

            var requestContext = _auditRequestContextProvider.GetContext();
            var txn = new ServiceAuditTransaction
            {
                ServiceName = _serviceName,
                EntityName = ResolveAuditEntityName(options),
                Operation = operation,
                Action = ResolveAuditAction(operation, options),
                EntityId = entityId,
                TenantId = CurrentTenantId(),
                ActorId = _auditActorProvider.GetActorId(),
                CorrelationId = requestContext.CorrelationId,
                IpAddress = requestContext.IpAddress,
                UserAgent = requestContext.UserAgent,
                DeviceId = requestContext.DeviceId,
                OriginalJson = CaptureBefore(operation, options) ? originalJson : null,
                TransformedJson = CaptureAfter(operation, options) ? transformedJson : null,
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
                EntityName = ResolveAuditEntityName(options),
                Operation = operation,
                Action = ResolveAuditAction(operation, options),
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
                await DemandServiceOperationAccessAsync(isUpdate ? ServiceAuditOperation.Update : ServiceAuditOperation.Create, ct).ConfigureAwait(false);
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

                if (isUpdates.Any(x => !x))
                    await DemandServiceOperationAccessAsync(ServiceAuditOperation.Create, ct).ConfigureAwait(false);
                if (isUpdates.Any(x => x))
                    await DemandServiceOperationAccessAsync(ServiceAuditOperation.Update, ct).ConfigureAwait(false);

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
                await DemandServiceOperationAccessAsync(ServiceAuditOperation.Archive, ct).ConfigureAwait(false);
                var auditOptions = CurrentAuditOptions;
                var shouldAudit = ShouldWriteTransactionAudit(ServiceAuditOperation.Archive, auditOptions);
                string? originalJson = null;
                if (shouldAudit && CaptureBefore(ServiceAuditOperation.Archive, auditOptions))
                {
                    var existing = await _repository.GetByIdAsync(id, includeArchived: true, includeDeleted: true, ct).ConfigureAwait(false);
                    originalJson = existing is not null ? SerializeEntity(existing) : null;
                }

                await PreArchiveAsync(id, ct).ConfigureAwait(false);
                await _repository.ArchiveAsync(id, ct).ConfigureAwait(false);

                string? transformedJson = null;
                if (shouldAudit && CaptureAfter(ServiceAuditOperation.Archive, auditOptions))
                {
                    var archived = await _repository.GetByIdAsync(id, includeArchived: true, includeDeleted: true, ct).ConfigureAwait(false);
                    transformedJson = archived is not null ? SerializeEntity(archived) : null;
                }

                await PostArchiveAsync(id, ct).ConfigureAwait(false);
                await TryWriteTransactionAuditAsync(ServiceAuditOperation.Archive, id, originalJson, transformedJson, ct).ConfigureAwait(false);
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
                await DemandServiceOperationAccessAsync(ServiceAuditOperation.Archive, ct).ConfigureAwait(false);
                var auditOptions = CurrentAuditOptions;
                var shouldAudit = ShouldWriteTransactionAudit(ServiceAuditOperation.Archive, auditOptions);
                var originalById = new Dictionary<Guid, string?>();
                if (shouldAudit && CaptureBefore(ServiceAuditOperation.Archive, auditOptions))
                {
                    var existingItems = await _repository.GetByIdsAsync(ids, includeArchived: true, includeDeleted: true, ct).ConfigureAwait(false);
                    foreach (var item in existingItems)
                    {
                        originalById[item.Id] = SerializeEntity(item);
                    }
                }

                foreach (var id in ids)
                    await PreArchiveAsync(id, ct).ConfigureAwait(false);

                await _repository.ArchiveAllAsync(ids, ct).ConfigureAwait(false);

                foreach (var id in ids)
                {
                    await PostArchiveAsync(id, ct).ConfigureAwait(false);

                    string? transformedJson = null;
                    if (shouldAudit && CaptureAfter(ServiceAuditOperation.Archive, auditOptions))
                    {
                        var archived = await _repository.GetByIdAsync(id, includeArchived: true, includeDeleted: true, ct).ConfigureAwait(false);
                        transformedJson = archived is not null ? SerializeEntity(archived) : null;
                    }

                    await TryWriteTransactionAuditAsync(
                        ServiceAuditOperation.Archive,
                        id,
                        originalById.TryGetValue(id, out var originalJson) ? originalJson : null,
                        transformedJson,
                        ct).ConfigureAwait(false);
                }
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
                await DemandServiceOperationAccessAsync(ServiceAuditOperation.Unarchive, ct).ConfigureAwait(false);
                var auditOptions = CurrentAuditOptions;
                var shouldAudit = ShouldWriteTransactionAudit(ServiceAuditOperation.Unarchive, auditOptions);
                string? originalJson = null;
                if (shouldAudit && CaptureBefore(ServiceAuditOperation.Unarchive, auditOptions))
                {
                    var existing = await _repository.GetByIdAsync(id, includeArchived: true, includeDeleted: true, ct).ConfigureAwait(false);
                    originalJson = existing is not null ? SerializeEntity(existing) : null;
                }

                await PreUnarchiveAsync(id, ct).ConfigureAwait(false);
                await _repository.UnarchiveAsync(id, ct).ConfigureAwait(false);

                string? transformedJson = null;
                if (shouldAudit && CaptureAfter(ServiceAuditOperation.Unarchive, auditOptions))
                {
                    var unarchived = await _repository.GetByIdAsync(id, includeArchived: true, includeDeleted: true, ct).ConfigureAwait(false);
                    transformedJson = unarchived is not null ? SerializeEntity(unarchived) : null;
                }

                await PostUnarchiveAsync(id, ct).ConfigureAwait(false);
                await TryWriteTransactionAuditAsync(ServiceAuditOperation.Unarchive, id, originalJson, transformedJson, ct).ConfigureAwait(false);
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
                await DemandServiceOperationAccessAsync(ServiceAuditOperation.Unarchive, ct).ConfigureAwait(false);
                var auditOptions = CurrentAuditOptions;
                var shouldAudit = ShouldWriteTransactionAudit(ServiceAuditOperation.Unarchive, auditOptions);
                var originalById = new Dictionary<Guid, string?>();
                if (shouldAudit && CaptureBefore(ServiceAuditOperation.Unarchive, auditOptions))
                {
                    var existingItems = await _repository.GetByIdsAsync(ids, includeArchived: true, includeDeleted: true, ct).ConfigureAwait(false);
                    foreach (var item in existingItems)
                    {
                        originalById[item.Id] = SerializeEntity(item);
                    }
                }

                foreach (var id in ids)
                    await PreUnarchiveAsync(id, ct).ConfigureAwait(false);

                await _repository.UnarchiveAllAsync(ids, ct).ConfigureAwait(false);

                foreach (var id in ids)
                {
                    await PostUnarchiveAsync(id, ct).ConfigureAwait(false);

                    string? transformedJson = null;
                    if (shouldAudit && CaptureAfter(ServiceAuditOperation.Unarchive, auditOptions))
                    {
                        var unarchived = await _repository.GetByIdAsync(id, includeArchived: true, includeDeleted: true, ct).ConfigureAwait(false);
                        transformedJson = unarchived is not null ? SerializeEntity(unarchived) : null;
                    }

                    await TryWriteTransactionAuditAsync(
                        ServiceAuditOperation.Unarchive,
                        id,
                        originalById.TryGetValue(id, out var originalJson) ? originalJson : null,
                        transformedJson,
                        ct).ConfigureAwait(false);
                }
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
                await DemandServiceOperationAccessAsync(ServiceAuditOperation.Delete, ct).ConfigureAwait(false);
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
