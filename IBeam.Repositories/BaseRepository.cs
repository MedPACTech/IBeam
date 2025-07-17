using System.Data;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ServiceStack.OrmLite;
using ServiceStack.OrmLite.Legacy;
using IBeam.Repositories.Interfaces;
using IBeam.Utilities;
using IBeam.DataModels.System;

namespace IBeam.Repositories
{
    public abstract class BaseRepository<T> : IBaseRepository<T> where T : class, IDTO
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

        /// <summary>
        /// Constructor for dependency injection with TenantContext.
        /// </summary>
        /// <param name="tenantContext">The TenantContext instance.</param>
        /// <param name="appSettings">Application settings.</param>
        /// <param name="memoryCache">Memory cache instance.</param>
        /// <param name="connectionStringOverride">Optional connection string override.</param>
        public BaseRepository(
            TenantContext tenantContext,
            IOptions<BaseAppSettings> appSettings,
            IMemoryCache memoryCache,
            string? connectionStringOverride = null)
        {
            if (tenantContext == null)
                throw new ArgumentNullException(nameof(tenantContext));

            _appSettings = appSettings?.Value ?? throw new ArgumentNullException(nameof(appSettings));
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _tenantContext = tenantContext;

            var repositoryType = typeof(T);
            RepositoryName = repositoryType.FullName.Replace("DTO", "Repository");
            RepositoryCacheName = $"{RepositoryName}Cache";
            IsTenantSpecific = typeof(IDTOTenant).IsAssignableFrom(repositoryType);
            IsArchivable = typeof(IDTOArchive).IsAssignableFrom(repositoryType);
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

        /// <summary>
        /// Gets the database type based on application settings.
        /// </summary>
        /// <returns>The appropriate OrmLite dialect provider.</returns>
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


        /// <summary>
        /// Initializes the setting for ID generation by the repository.
        /// </summary>
        /// <returns>True if the repository generates IDs, otherwise false.</returns>
        private bool InitializeIdGenerationSetting()
        {
            return _appSettings.IdGeneratedByRepository ?? false;
        }

        /// <summary>
        /// Initializes the cache setting for the repository.
        /// </summary>
        /// <returns>True if caching is enabled, otherwise false.</returns>
        private bool InitializeCacheSetting()
        {
            return _appSettings.EnableCache ?? true;
        }

        /// <summary>
        /// Determines if soft delete is disabled for the repository.
        /// </summary>
        /// <param name="disableSoftDelete">Global disable soft delete setting.</param>
        /// <param name="repositoryType">The type of the repository.</param>
        /// <returns>True if soft delete is disabled, otherwise false.</returns>
        private bool DetermineSoftDelete(string? disableSoftDelete, Type repositoryType)
        {
            if (string.IsNullOrWhiteSpace(disableSoftDelete))
            {
                return typeof(IDTODelete).IsAssignableFrom(repositoryType);
            }

            if (bool.TryParse(disableSoftDelete, out var globalSetting))
            {
                return globalSetting;
            }

            return typeof(IDTODelete).IsAssignableFrom(repositoryType);
        }

        /// <summary>
        /// Validates that the TenantId is set for tenant-specific operations.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if TenantId is not set.</exception>
        private void ValidateTenantId()
        {
            if (IsTenantSpecific && !_tenantContext.IsTenantIdSet())
            {
                throw new InvalidOperationException($"TenantId is required for {RepositoryName} but is not set in the current context.");
            }
        }

        private void ClearCache()
        {
            if (EnableCache)
                _memoryCache.Remove(RepositoryCacheName);
        }

        private List<T> GetAllCached()
        {
            var results = _memoryCache.Get<List<T>>(RepositoryCacheName);

            if (results == null)
            {
                var newResults = GetAllWithArchive(false);
                _memoryCache.Set(RepositoryCacheName, newResults);
                results = newResults;
            }

            return results;
        }

        private List<T> GetAllWithArchive(bool withArchived)
        {
            using IDbConnection db = _dataFactory.OpenDbConnection();

            var query = db.From<T>();

            if (!withArchived)
                query = ApplyCommonFilters(query);
            else
            {
                if (IsTenantSpecific)
                    query = query.And(x => ((IDTOTenant)x).TenantId == _tenantContext.TenantId);

                if (!IsSoftDeleteDisabled && typeof(IDTODelete).IsAssignableFrom(typeof(T)))
                    query = query.And(x => ((IDTODelete)x).IsDeleted == false);
            }


            return db.Select(query);
        }

        /// <summary>
        /// Returns a list of all records from the repository.
        /// If the entity inherits IDTOTenant, the TenantId must match the repository's TenantId.
        /// If EnableCache is true, the results will be cached unless withArchived is true.
        /// </summary>
        /// <param name="withArchived"></param>
        /// <returns></returns>
        /// <exception cref="RepositoryException"></exception>
        public virtual List<T> GetAll(bool withArchived = false)
        {
            try
            {
                ValidateTenantId();

                if (EnableCache && withArchived)
                    throw new Exception("Get All cannot cache archived results"); 

                if (EnableCache)
                    return GetAllCached();
                else
                    return GetAllWithArchive(withArchived);
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "GetAll");
            }
        }

        /// <summary>
        /// Returns a list of records from the repository by Ids.
        /// If the entity inherits IDTOTenant, the TenantId must match the repository's TenantId.
        /// </summary>
        /// <param name="ids"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="RepositoryException"></exception>
        public virtual List<T> GetByIds(List<Guid> ids)
        {
            try
            {
                ValidateTenantId();

                if (ids == null || !ids.Any())
                {
                    return Enumerable.Empty<T>().ToList();
                }

                using IDbConnection db = _dataFactory.OpenDbConnection();
                var query = db.From<T>().Where(x => Sql.In(x.Id, ids));
                query = ApplyCommonFilters(query);

                return db.Select(query);
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "GetByIds", null, ids);
            }
        }

        /// <summary>
        /// Returns a single record from the repository by Id.
        /// If the entity inherits IDTOTenant, the TenantId must match the repository's TenantId.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        /// <exception cref="KeyNotFoundException"></exception>
        /// <exception cref="RepositoryException"></exception>
        public T GetById(Guid id)
        {
            try
            {
                ValidateTenantId();

                using IDbConnection db = _dataFactory.OpenDbConnection();
                var query = db.From<T>().Where(x => x.Id == id);
                query = ApplyCommonFilters(query);

                var result = db.Single(query);
                if (result == null)
                {
                    throw new KeyNotFoundException($"Entity of type {typeof(T).Name} with Id {id} not found.");
                }

                return result;
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "GetById", null, id);
            }
        }
        
        /// <summary>
        /// Builds a safe query for type T with tenant, archive, and soft delete filters pre-applied.
        /// Call .Select(query) with your custom clauses on top.
        /// </summary>
        /// <param name="dbConnection">Optional DB connection to use for the query.</param>
        /// <returns>SqlExpression with filters applied.</returns>
        public SqlExpression<T> Query(
            IDbConnection? dbConnection = null,
            bool includeArchived = false,
            bool includeDeleted = false)
        {
            ValidateTenantId();
            var db = dbConnection ?? _dataFactory.OpenDbConnection();
            var query = db.From<T>();

            return ApplyCommonFilters(query, includeArchived, includeDeleted);
        }

        /// <summary>
        /// Executes a filtered query using a provided expression builder. 
        /// Automatically applies common filters (TenantId, IsArchived, IsDeleted),
        /// unless overridden via parameters.
        /// </summary>
        /// <param name="expressionBuilder">A function to build on top of the base query.</param>
        /// <param name="includeArchived">Set to true to include archived records.</param>
        /// <param name="includeDeleted">Set to true to include soft-deleted records.</param>
        /// <returns>A list of filtered results.</returns>
        public List<T> QueryWhere(
            Func<SqlExpression<T>, SqlExpression<T>> expressionBuilder,
            bool includeArchived = false,
            bool includeDeleted = false)
        {
            ValidateTenantId();

            using var db = _dataFactory.OpenDbConnection();
            var baseQuery = db.From<T>();
            var filteredQuery = ApplyCommonFilters(baseQuery, includeArchived, includeDeleted);
            var finalQuery = expressionBuilder(filteredQuery);

            return db.Select(finalQuery);
        }

        /// <summary>
        /// Saves a single record in the repository.
        /// If the entity inherits IDTOTenant, the TenantId must match the repository's TenantId.
        /// if IdGeneratedByRepository is true, a new Guid will be generated for the Id.
        /// </summary>
        /// <param name="dto"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="RepositoryException"></exception>
        public T Save(T dto)
        {
            try
            {
                ValidateTenantId();

                using IDbConnection db = _dataFactory.OpenDbConnection();

                if (!IdGeneratedByRepository)
                {
                    if (dto.Id == Guid.Empty)
                    {
                        throw new Exception("Empty Id is not allowed when Ids are managed externally.");
                    }
                }
                else
                {
                    if (dto.Id == Guid.Empty)
                    {
                        dto.Id = Guid.NewGuid();
                    }
                }

                if (IsTenantSpecific)
                {
                    if (dto is IDTOTenant tenantDto)
                    {

                        if (tenantDto.TenantId == Guid.Empty)
                        {
                            tenantDto.TenantId = _tenantContext.TenantId.Value;
                        }

                        if (tenantDto.TenantId != _tenantContext.TenantId)
                        {
                            throw new InvalidOperationException($"TenantId mismatch. Entity belongs to TenantId {tenantDto.TenantId}, but repository is for TenantId {_tenantContext.TenantId}.");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException($"Entity of type {typeof(T).Name} does not implement IDTOTenant, but the repository is tenant-specific.");
                    }
                }

                db.Save(dto);
                ClearCache();

                return dto;
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "Save", dto);
            }
        }

        /// <summary>
        /// Saves a list of records in the repository.
        /// If the entity inherits IDTOTenant, the TenantId must match the repository's TenantId.
        /// If IdGeneratedByRepository is true, a new Guid will be generated for the Id.
        /// </summary>
        /// <param name="dtos"></param>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="RepositoryException"></exception>
        public List<T> SaveAll(List<T> dtos)
        {
            try
            {
                ValidateTenantId();

                using IDbConnection db = _dataFactory.OpenDbConnection();

                foreach (var dto in dtos)
                {
                    if (!IdGeneratedByRepository)
                    {
                        if (dto.Id == Guid.Empty)
                        {
                            throw new Exception("Empty Ids are not allowed when Ids are managed externally.");
                        }
                    }
                    else
                    {
                        if (dto.Id == Guid.Empty)
                        {
                            dto.Id = Guid.NewGuid();
                        }
                    }

                    if (IsTenantSpecific)
                    {
                        if (dto is IDTOTenant tenantDto)
                        {
                            if (tenantDto.TenantId == Guid.Empty)
                            {
                                tenantDto.TenantId = _tenantContext.TenantId.Value;
                            }

                            if (tenantDto.TenantId != _tenantContext.TenantId)
                            {
                                throw new InvalidOperationException($"TenantId mismatch. Entity belongs to TenantId {tenantDto.TenantId}, but repository is for TenantId {_tenantContext.TenantId}.");
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException($"Entity of type {typeof(T).Name} does not implement IDTOTenant, but the repository is tenant-specific.");
                        }
                    }
                }

                db.SaveAll(dtos);
                ClearCache();
                return dtos;
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "SaveAll", dtos);
            }
        }

       /// <summary>
        /// Archives a single record in the repository.
        /// <paramref name="dto"/> must implement IDTOArchive.
        /// Only IsArchived will be updated to true.
        /// </summary>
        public bool Archive(T dto)
        {
            try
            {
                var entity = GetById(dto.Id);

                if (entity is not IDTOArchive archiveEntity)
                    throw new InvalidOperationException($"Entity of type {typeof(T).Name} does not implement IDTOArchive.");

                archiveEntity.IsArchived = true;

                Save(entity);
                return true;
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "Archive", dto);
            }
        }


        /// <summary>
        /// Archives all records in the repository.
        /// Only IsArchived will be updated to true.
        /// </summary>
        public bool ArchiveAll(List<T> dtos)
        {
            try
            {
                var ids = dtos.Select(x => x.Id).ToList();
                var entities = GetByIds(ids);

                foreach (var entity in entities)
                {
                    if (entity is not IDTOArchive archiveEntity)
                        throw new InvalidOperationException($"Entity of type {typeof(T).Name} does not implement IDTOArchive.");

                    archiveEntity.IsArchived = true;
                }

                SaveAll(entities);
                return true;
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "ArchiveAll", dtos);
            }
        }

        /// <summary>
        /// Unarchives a single record in the repository.
        /// Only IsArchived will be updated to false.
        /// </summary>
        public bool Unarchive(T dto)
        {
            try
            {
                var entity = GetById(dto.Id);

                if (entity is not IDTOArchive archiveEntity)
                    throw new InvalidOperationException($"Entity of type {typeof(T).Name} does not implement IDTOArchive.");

                archiveEntity.IsArchived = false;

                Save(entity);
                return true;
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "UnArchive", dto);
            }
        }


        /// <summary>
        /// Unarchives all records in the repository.
        /// Only IsArchived will be updated to false.
        /// </summary>
        public bool UnarchiveAll(List<T> dtos)
        {
            try
            {
                var ids = dtos.Select(x => x.Id).ToList();
                var entities = GetByIds(ids);

                foreach (var entity in entities)
                {
                    if (entity is not IDTOArchive archiveEntity)
                        throw new InvalidOperationException($"Entity of type {typeof(T).Name} does not implement IDTOArchive.");

                    archiveEntity.IsArchived = false;
                }

                SaveAll(entities);
                return true;
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "UnArchiveAll", dtos);
            }
        }

        /// <summary>
        /// Deletes a single record in the repository by entity.
        /// If entity inherits IDOTenant, the TenantId must match the repository's TenantId.
        /// If entity inherits IDTODelete, a hard delete will be performed.
        /// </summary>
        /// <param name="dto"></param>
        /// <exception cref="RepositoryException"></exception>
        public void Delete(T dto)
        {
            try
            {
                // Use GetById to validate and retrieve the entity
                var entity = GetById(dto.Id);

                using IDbConnection db = _dataFactory.OpenDbConnection();

                // Determine if we should perform a hard delete
                if (typeof(IDTODelete).IsAssignableFrom(typeof(T)) || IsSoftDeleteDisabled)
                {
                    // Perform hard delete
                    db.Delete<T>(entity);
                }
                else
                {
                    entity.IsDeleted = true;
                    db.Update(entity);
                }

                ClearCache();
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "DeleteById", dto.Id);
            }
        }

        /// <summary>
        /// Deletes a single record in the repository by Id.
        /// If Entity inherits IDOTenant, the TenantId must match the repository's TenantId.
        /// If Entity inherits IDTODelete, a hard delete will be performed.
        /// </summary>
        /// <param name="id"></param>
        /// <exception cref="RepositoryException"></exception>
        public void DeleteById(Guid id)
        {
            try
            {
                // Use GetById to validate and retrieve the entity
                var entity = GetById(id);

                using IDbConnection db = _dataFactory.OpenDbConnection();

                // Determine if we should perform a hard delete
                if (typeof(IDTODelete).IsAssignableFrom(typeof(T)) || IsSoftDeleteDisabled)
                {
                    // Perform hard delete
                    db.DeleteById<T>(id);
                }
                else
                {
                    entity.IsDeleted = true;
                    db.Update(entity);
                }

                ClearCache();
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "DeleteById", id);
            }
        }

        private static IOrmLiteDialectProvider ConfigureSqliteDialect()
        {
            var sqlite = SqliteDialect.Provider;

            sqlite.RegisterConverter<Guid>(new SqliteGuidAsStringConverter());

            return sqlite;
        }

        private SqlExpression<T> ApplyCommonFilters(
            SqlExpression<T> query,
            bool includeArchived = false,
            bool includeDeleted = false)
        {
            if (IsTenantSpecific)
            {
                query = query.And(x => ((IDTOTenant)x).TenantId == _tenantContext.TenantId);
            }

            if (IsArchivable && !includeArchived)
            {
                query = query.And(x => ((IDTOArchive)x).IsArchived == false);
            }

            if (!IsSoftDeleteDisabled && !includeDeleted)
            {
                query = query.And(x => x.IsDeleted == false);
            }

            return query;
        }


        public class SqliteGuidAsStringConverter : OrmLiteConverter
        {
            // Show "TEXT" or "UUID" in the CREATE TABLE schema
            public override string ColumnDefinition => "TEXT";  // Or "UUID"

            // ADO.NET type for parameterized queries
            public override DbType DbType => DbType.String;

            // How to store Guid in the DB (as a string)
            public override object ToDbValue(Type fieldType, object value)
                => value?.ToString();

            // Convert back to Guid on retrieval
            public override object FromDbValue(Type fieldType, object value)
                => value == null ? Guid.Empty : new Guid(value.ToString());

            // Quote values for SQL statements
            public override string ToQuotedString(Type fieldType, object value)
                => $"'{value}'";
        }


    }
}
