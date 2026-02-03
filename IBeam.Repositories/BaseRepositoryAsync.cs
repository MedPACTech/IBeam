using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IBeam.DataModels.System;              // IDTO, IDTOTenant, IDTOArchive, IDTODelete
using IBeam.Utilities;                      // RepositoryException
using IBeam.Utilities.Exceptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ServiceStack.OrmLite;
using ServiceStack.OrmLite.Legacy;          // for Sql.In

namespace IBeam.Repositories
{
    public abstract class BaseRepositoryAsync<T> : Interfaces.IBaseRepositoryAsync<T>
        where T : class, IEntity
    {
        public string RepositoryName { get; }
        public string RepositoryCacheName { get; }
        public bool IsArchivable { get; }
        public bool IsSoftDeleteDisabled { get; }
        public bool IsTenantSpecific { get; }
        public bool EnableCache { get; set; }
        public bool IdGeneratedByRepository { get; }

        protected readonly OrmLiteConnectionFactory _dataFactory;
        protected readonly IMemoryCache _memoryCache;
        protected readonly BaseAppSettings _appSettings;
        protected readonly TenantContext _tenantContext;

        protected BaseRepositoryAsync(
            TenantContext tenantContext,
            IOptions<BaseAppSettings> appSettings,
            IMemoryCache memoryCache,
            string? connectionStringOverride = null)
        {
            if (tenantContext == null) throw new ArgumentNullException(nameof(tenantContext));

            _appSettings = appSettings?.Value ?? throw new ArgumentNullException(nameof(appSettings));
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _tenantContext = tenantContext;

            var repositoryType = typeof(T);
            RepositoryName = repositoryType.FullName!.Replace("DTO", "Repository");
            RepositoryCacheName = $"{RepositoryName}Cache";

            IsTenantSpecific = typeof(ITenantEntity).IsAssignableFrom(repositoryType);
            IsArchivable = typeof(IArchivableEntity).IsAssignableFrom(repositoryType);
            IsSoftDeleteDisabled = DetermineSoftDelete(_appSettings.DisableSoftDelete, repositoryType);
            EnableCache = InitializeCacheSetting();
            IdGeneratedByRepository = InitializeIdGenerationSetting();

            var dialectProvider = GetDatabaseType();
            string connectionString = connectionStringOverride ?? _appSettings.ConnectionString;

            _dataFactory = new OrmLiteConnectionFactory(connectionString, dialectProvider);
            _dataFactory.DialectProvider.NamingStrategy = new OrmLiteNamingStrategyBase();

            if (IsTenantSpecific && !_tenantContext.IsTenantIdSet())
            {
                throw new InvalidOperationException($"TenantId is required for {RepositoryName} but is not set in the TenantContext.");
            }
        }

        public async Task<List<T>> GetAllAsync(bool withArchived = false, CancellationToken ct = default)
        {
            try
            {
                ValidateTenantId();

                if (EnableCache && withArchived)
                    throw new Exception("GetAllAsync cannot cache archived results");

                if (EnableCache && !withArchived)
                    return await GetAllCachedAsync(ct);

                return await GetAllWithArchiveAsync(withArchived, ct);
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, nameof(GetAllAsync), new { withArchived });
            }
        }

        public async Task<List<T>> GetByIdsAsync(List<Guid> ids, CancellationToken ct = default)
        {
            try
            {
                ValidateTenantId();

                if (ids == null || ids.Count == 0)
                    return new List<T>();

                using var db = await _dataFactory.OpenDbConnectionAsync(ct);
                var query = db.From<T>().Where(x => Sql.In(x.Id, ids));
                query = ApplyCommonFilters(query);

                return await db.SelectAsync(query, token: ct);
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, nameof(GetByIdsAsync), null, ids);
            }
        }

        public async Task<T> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            try
            {
                ValidateTenantId();

                using var db = await _dataFactory.OpenDbConnectionAsync(ct);
                var query = db.From<T>().Where(x => x.Id == id);
                query = ApplyCommonFilters(query);

                var result = await db.SingleAsync(query, token: ct);
                if (result == null)
                    throw new KeyNotFoundException($"Entity of type {typeof(T).Name} with Id {id} not found.");

                return result;
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, nameof(GetByIdAsync), null, id);
            }
        }

        public async Task<List<T>> QueryWhereAsync(
            Func<SqlExpression<T>, SqlExpression<T>> expressionBuilder,
            bool includeArchived = false,
            bool includeDeleted = false,
            CancellationToken ct = default)
        {
            try
            {
                ValidateTenantId();

                using var db = await _dataFactory.OpenDbConnectionAsync(ct);
                var baseQuery = db.From<T>();
                var filtered = ApplyCommonFilters(baseQuery, includeArchived, includeDeleted);
                var finalQuery = expressionBuilder(filtered);

                return await db.SelectAsync(finalQuery, token: ct);
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, nameof(QueryWhereAsync), new { includeArchived, includeDeleted });
            }
        }

        public async Task<TResult> WithConnectionAsync<TResult>(
            Func<IDbConnection, Task<TResult>> work,
            CancellationToken ct = default)
        {
            try
            {
                using var db = await _dataFactory.OpenDbConnectionAsync(ct);
                return await work(db);
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, nameof(WithConnectionAsync));
            }
        }

        public async Task<T> SaveAsync(T dto, CancellationToken ct = default)
        {
            try
            {
                ValidateTenantId();

                using var db = await _dataFactory.OpenDbConnectionAsync(ct);

                // Id policy
                if (!IdGeneratedByRepository)
                {
                    if (dto.Id == Guid.Empty)
                        throw new Exception("Empty Id is not allowed when Ids are managed externally.");
                }
                else if (dto.Id == Guid.Empty)
                {
                    dto.Id = Guid.NewGuid();
                }

                // Tenant policy (if tenant-specific)
                if (IsTenantSpecific)
                {
                    if (dto is ITenantEntity tenantDto)
                    {
                        if (tenantDto.TenantId == Guid.Empty)
                            tenantDto.TenantId = _tenantContext.TenantId!.Value;

                        if (tenantDto.TenantId != _tenantContext.TenantId)
                            throw new InvalidOperationException($"TenantId mismatch. Entity belongs to TenantId {tenantDto.TenantId}, but repository is for TenantId {_tenantContext.TenantId}.");
                    }
                    else
                    {
                        throw new InvalidOperationException($"Entity of type {typeof(T).Name} does not implement {nameof(ITenantEntity)}, but the repository is tenant-specific.");
                    }
                }

                await db.SaveAsync(dto, token: ct);
                ClearCache();
                return dto;
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, nameof(SaveAsync), dto);
            }
        }

        public async Task<List<T>> SaveAllAsync(List<T> dtos, CancellationToken ct = default)
        {
            try
            {
                ValidateTenantId();

                using var db = await _dataFactory.OpenDbConnectionAsync(ct);

                foreach (var dto in dtos)
                {
                    // Id policy
                    if (!IdGeneratedByRepository)
                    {
                        if (dto.Id == Guid.Empty)
                            throw new Exception("Empty Ids are not allowed when Ids are managed externally.");
                    }
                    else if (dto.Id == Guid.Empty)
                    {
                        dto.Id = Guid.NewGuid();
                    }

                    // Tenant policy
                    if (IsTenantSpecific)
                    {
                        if (dto is ITenantEntity tenantDto)
                        {
                            if (tenantDto.TenantId == Guid.Empty)
                                tenantDto.TenantId = _tenantContext.TenantId!.Value;

                            if (tenantDto.TenantId != _tenantContext.TenantId)
                                throw new InvalidOperationException($"TenantId mismatch. Entity belongs to TenantId {tenantDto.TenantId}, but repository is for TenantId {_tenantContext.TenantId}.");
                        }
                        else
                        {
                            throw new InvalidOperationException($"Entity of type {typeof(T).Name} does not implement {nameof(ITenantEntity)}, but the repository is tenant-specific.");
                        }
                    }
                }

                await db.SaveAllAsync(dtos, token: ct);
                ClearCache();
                return dtos;
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, nameof(SaveAllAsync), dtos);
            }
        }

        public async Task<bool> ArchiveAsync(T dto, CancellationToken ct = default)
        {
            try
            {
                var entity = await GetByIdAsync(dto.Id, ct);

                if (entity is not IArchivableEntity archiveEntity)
                    throw new InvalidOperationException($"Entity of type {typeof(T).Name} does not implement {nameof(IArchivableEntity)}.");

                archiveEntity.IsArchived = true;

                await SaveAsync(entity, ct);
                return true;
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, nameof(ArchiveAsync), dto);
            }
        }

        public async Task<bool> ArchiveAllAsync(List<T> dtos, CancellationToken ct = default)
        {
            try
            {
                var ids = dtos.Select(x => x.Id).ToList();
                var entities = await GetByIdsAsync(ids, ct);

                foreach (var entity in entities)
                {
                    if (entity is not IArchivableEntity archiveEntity)
                        throw new InvalidOperationException($"Entity of type {typeof(T).Name} does not implement {nameof(IArchivableEntity)}.");

                    archiveEntity.IsArchived = true;
                }

                await SaveAllAsync(entities, ct);
                return true;
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, nameof(ArchiveAllAsync), dtos);
            }
        }

        public async Task<bool> UnarchiveAsync(T dto, CancellationToken ct = default)
        {
            try
            {
                var entity = await GetByIdAsync(dto.Id, ct);

                if (entity is not IArchivableEntity archiveEntity)
                    throw new InvalidOperationException($"Entity of type {typeof(T).Name} does not implement {nameof(IArchivableEntity)}.");

                archiveEntity.IsArchived = false;

                await SaveAsync(entity, ct);
                return true;
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, nameof(UnarchiveAsync), dto);
            }
        }

        public async Task<bool> UnarchiveAllAsync(List<T> dtos, CancellationToken ct = default)
        {
            try
            {
                var ids = dtos.Select(x => x.Id).ToList();
                var entities = await GetByIdsAsync(ids, ct);

                foreach (var entity in entities)
                {
                    if (entity is not IArchivableEntity archiveEntity)
                        throw new InvalidOperationException($"Entity of type {typeof(T).Name} does not implement {nameof(IArchivableEntity)}.");

                    archiveEntity.IsArchived = false;
                }

                await SaveAllAsync(entities, ct);
                return true;
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, nameof(UnarchiveAllAsync), dtos);
            }
        }

        public async Task DeleteByIdAsync(Guid id, CancellationToken ct = default)
        {
            try
            {
                // validate & load entity (ensures tenant/filters)
                var entity = await GetByIdAsync(id, ct);

                using var db = await _dataFactory.OpenDbConnectionAsync(ct);

                if (typeof(IDeletableEntity).IsAssignableFrom(typeof(T)) || IsSoftDeleteDisabled)
                {
                    // hard delete
                    await db.DeleteByIdAsync<T>(id, token: ct);
                }
                else
                {
                    // soft delete
                    entity.IsDeleted = true;
                    await db.UpdateAsync(entity, token: ct);
                }

                ClearCache();
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, nameof(DeleteByIdAsync), id);
            }
        }

        // -------------------------- Internals --------------------------

        private async Task<List<T>> GetAllCachedAsync(CancellationToken ct)
        {
            if (!EnableCache)
                return await GetAllWithArchiveAsync(false, ct);

            // IMemoryCache lacks native token-aware API on GetOrCreate*, but we can ignore 'ct' for cache fetch.
            if (_memoryCache.TryGetValue(RepositoryCacheName, out List<T> cached) && cached != null)
                return cached;

            var fresh = await GetAllWithArchiveAsync(false, ct);
            _memoryCache.Set(RepositoryCacheName, fresh);
            return fresh;
        }

        private async Task<List<T>> GetAllWithArchiveAsync(bool withArchived, CancellationToken ct)
        {
            using var db = await _dataFactory.OpenDbConnectionAsync(ct);

            var query = db.From<T>();

            if (!withArchived)
                query = ApplyCommonFilters(query);
            else
            {
                if (IsTenantSpecific)
                    query = query.And(x => ((ITenantEntity)x).TenantId == _tenantContext.TenantId);

                if (!IsSoftDeleteDisabled && typeof(IDeletableEntity).IsAssignableFrom(typeof(T)))
                    query = query.And(x => ((IDeletableEntity)x).IsDeleted == false);
            }

            return await db.SelectAsync(query, token: ct);
        }

        private void ClearCache()
        {
            if (EnableCache)
                _memoryCache.Remove(RepositoryCacheName);
        }

        private void ValidateTenantId()
        {
            if (IsTenantSpecific && !_tenantContext.IsTenantIdSet())
                throw new InvalidOperationException($"TenantId is required for {RepositoryName} but is not set in the current context.");
        }

        private bool InitializeIdGenerationSetting() => _appSettings.IdGeneratedByRepository ?? false;
        private bool InitializeCacheSetting() => _appSettings.EnableCache ?? true;

        private bool DetermineSoftDelete(string? disableSoftDelete, Type repositoryType)
        {
            if (string.IsNullOrWhiteSpace(disableSoftDelete))
                return typeof(IDeletableEntity).IsAssignableFrom(repositoryType);

            if (bool.TryParse(disableSoftDelete, out var globalSetting))
                return globalSetting;

            return typeof(IDeletableEntity).IsAssignableFrom(repositoryType);
        }

        private IOrmLiteDialectProvider GetDatabaseType()
        {
            var databaseType = _appSettings.DatabaseType?.Trim().ToUpperInvariant();

            return databaseType switch
            {
                "MSSQL" => SqlServerDialect.Provider,
                "POSTGRESQL" => PostgreSqlDialect.Provider,
                "SQLITE3" => ConfigureSqliteDialect(),
                _ => throw new Exception($"Unrecognized database type '{_appSettings.DatabaseType}'")
            };
        }

        private SqlExpression<T> ApplyCommonFilters(
            SqlExpression<T> query,
            bool includeArchived = false,
            bool includeDeleted = false)
        {
            if (IsTenantSpecific)
                query = query.And(x => ((ITenantEntity)x).TenantId == _tenantContext.TenantId);

            if (IsArchivable && !includeArchived)
                query = query.And(x => ((IArchivableEntity)x).IsArchived == false);

            if (!IsSoftDeleteDisabled && !includeDeleted)
                query = query.And(x => x.IsDeleted == false);

            return query;
        }

        private static IOrmLiteDialectProvider ConfigureSqliteDialect()
        {
            var sqlite = SqliteDialect.Provider;
            sqlite.RegisterConverter<Guid>(new SqliteGuidAsStringConverter());
            return sqlite;
        }

        public class SqliteGuidAsStringConverter : OrmLiteConverter
        {
            public override string ColumnDefinition => "TEXT";
            public override DbType DbType => DbType.String;
            public override object ToDbValue(Type fieldType, object value) => value?.ToString();
            public override object FromDbValue(Type fieldType, object value)
                => value == null ? Guid.Empty : new Guid(value.ToString());
            public override string ToQuotedString(Type fieldType, object value) => $"'{value}'";
        }
    }
}
