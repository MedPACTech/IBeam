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
            return _appSettings.DatabaseType switch
            {
                "MSSql" => SqlServerDialect.Provider,
                "Postgres" => PostgreSqlDialect.Provider,
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
            if (IsTenantSpecific)
            {
                query = query.Where(x => ((IDTOTenant)x).TenantId == _tenantContext.TenantId);
            }

            if (IsArchivable && !withArchived)
            {
                query = query.Where(x => ((IDTOArchive)x).IsArchived == false);
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
                    throw new ArgumentException("The list of IDs cannot be null or empty.", nameof(ids));
                }

                using IDbConnection db = _dataFactory.OpenDbConnection();
                var query = db.From<T>().Where(x => Sql.In(x.Id, ids));

                if (IsTenantSpecific)
                {
                    query = query.Where(x => ((IDTOTenant)x).TenantId == _tenantContext.TenantId);
                }

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

                if (IsTenantSpecific)
                {
                    query = query.Where(x => ((IDTOTenant)x).TenantId == _tenantContext.TenantId);
                }

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

        public bool Archive(T dto)
        {
            try
            {
                if (dto is not IDTOArchive dtoArchive)
                {
                    throw new InvalidOperationException($"The entity of type {typeof(T).Name} does not implement IDTOArchive and cannot be archived.");
                }

                dtoArchive.IsArchived = true;

                Save(dto);

                return true;
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "Archive", dto);
            }
        }

        public bool ArchiveAll(List<T> dtos)
        {
            try
            {
                foreach (var dto in dtos)
                {
                    if (dto is not IDTOArchive dtoArchive)
                    {
                        throw new InvalidOperationException($"The entity of type {typeof(T).Name} does not implement IDTOArchive and cannot be archived.");
                    }

                    dtoArchive.IsArchived = true;
                }

                SaveAll(dtos);

                return true;
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "ArchiveAll", dtos);
            }
        }

        public bool UnArchive(T dto)
        {
            try
            {
                if (dto is not IDTOArchive dtoArchive)
                {
                    throw new InvalidOperationException($"The entity of type {typeof(T).Name} does not implement IDTOArchive and cannot be unarchived.");
                }

                dtoArchive.IsArchived = false;

                Save(dto);

                return true;
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "Archive", dto);
            }
        }

        /// <summary>
        /// Unarchives all records in the repository.
        /// </summary>
        /// <param name="dtos"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="RepositoryException"></exception>
        public bool UnArchiveAll(List<T> dtos)
        {
            try
            {
                foreach (var dto in dtos)
                {
                    if (dto is not IDTOArchive dtoArchive)
                    {
                        throw new InvalidOperationException($"The entity of type {typeof(T).Name} does not implement IDTOArchive and cannot be archived.");
                    }

                    dtoArchive.IsArchived = true;
                }

                SaveAll(dtos);

                return true;
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "ArchiveAll", dtos);
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
    }
}
