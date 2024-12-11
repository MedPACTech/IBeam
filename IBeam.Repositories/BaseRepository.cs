
using System.Data;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using IBeam.Repositories.Interfaces;
using ServiceStack.OrmLite;
using ServiceStack.OrmLite.Legacy;
using IBeam.Utilities;
using IBeam.DataModels.System;

//todo: abstract out ormlite inject into ormlite Base if possible
namespace IBeam.Repositories
{
    public abstract class BaseRepository<T> : IBaseRepository<T> where T : class, IDTO
    {
        public string RepositoryName { get; }
        public string RepositoryCacheName { get; }
        public bool IsArchivable { get; }
        public bool IsSoftDeleteDisabled { get; }
        public bool IsTenantSpecific { get; }
        public Guid? TenantId { get; } // TenantId set during construction
        public bool EnableCache { get; set; }
        public bool IdGeneratedByRepository { get; }

        protected readonly OrmLiteConnectionFactory _dataFactory;
        protected readonly IMemoryCache _memoryCache;
        protected readonly BaseAppSettings _appSettings;

        public BaseRepository(
            IOptions<BaseAppSettings> appSettings,
            IMemoryCache memoryCache,
            Guid? tenantId = null, // TenantId passed as a parameter
            string? connectionStringOverride = null)
        {
            _appSettings = appSettings.Value;
            var repositoryType = typeof(T);

            // Initialize repository name and cache name
            RepositoryName = repositoryType.FullName.Replace("DTO", "Repository");
            RepositoryCacheName = $"{RepositoryName}Cache";

            // Determine repository-specific flags
            IsTenantSpecific = typeof(IDTOTenant).IsAssignableFrom(repositoryType);
            IsArchivable = typeof(IDTOArchive).IsAssignableFrom(repositoryType);
            IsSoftDeleteDisabled = DetermineSoftDelete(_appSettings.DisableSoftDelete, repositoryType);
            TenantId = InitializeTenantSetting(tenantId);
            EnableCache = InitializeCacheSetting();
            IdGeneratedByRepository = InitializeIdGenerationSetting();

            // Set up database connection
            var dialectProvider = GetDatabaseType();
            string connectionString = connectionStringOverride ?? _appSettings.ConnectionString;

            _dataFactory = new OrmLiteConnectionFactory(connectionString, dialectProvider);
            _dataFactory.DialectProvider.NamingStrategy = new OrmLiteNamingStrategyBase();
            _memoryCache = memoryCache;
        }

        private IOrmLiteDialectProvider GetDatabaseType()
        {
            return _appSettings.DatabaseType switch
            {
                "MSSql" => SqlServerDialect.Provider,
                "Postgres" => PostgreSqlDialect.Provider,
                _ => throw new Exception($"Unrecognized database type '{_appSettings.DatabaseType}'")
            };
        }

        private Guid InitializeTenantSetting(Guid? tenantId)
        {
            if (IsTenantSpecific && (!tenantId.HasValue))
            {
                throw new InvalidOperationException($"TenantId is required for {RepositoryName} but was not provided during repository creation.");
            }

            if (tenantId.HasValue) 
                return tenantId.Value;
            return Guid.Empty;
        }

        private bool InitializeIdGenerationSetting()
        {
            return _appSettings.IdGeneratedByRepository ?? false;
        }

        private bool InitializeCacheSetting()
        {
            return _appSettings.EnableCache ?? true;
        }

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
                query = query.Where(x => ((IDTOTenant)x).TenantId == TenantId.Value);
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
        public virtual IEnumerable<T> GetAll(bool withArchived = false)
        {
            try
            {
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
                if (ids == null || !ids.Any())
                {
                    throw new ArgumentException("The list of IDs cannot be null or empty.", nameof(ids));
                }

                using IDbConnection db = _dataFactory.OpenDbConnection();
                var query = db.From<T>().Where(x => Sql.In(x.Id, ids));

                if (IsTenantSpecific)
                {
                    query = query.Where(x => ((IDTOTenant)x).TenantId == TenantId.Value);
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
                using IDbConnection db = _dataFactory.OpenDbConnection();
                var query = db.From<T>().Where(x => x.Id == id);

                if (IsTenantSpecific)
                {
                    query = query.Where(x => ((IDTOTenant)x).TenantId == TenantId.Value);
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
                        if (tenantDto.TenantId != TenantId.Value)
                        {
                            throw new InvalidOperationException($"TenantId mismatch. Entity belongs to TenantId {tenantDto.TenantId}, but repository is for TenantId {TenantId}.");
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
        public void SaveAll(List<T> dtos)
        {
            try
            {
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
                            if (tenantDto.TenantId != TenantId.Value)
                            {
                                throw new InvalidOperationException($"TenantId mismatch. Entity belongs to TenantId {tenantDto.TenantId}, but repository is for TenantId {TenantId}.");
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
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "SaveAll", dtos);
            }
        }


        public bool Archive(T dto)
        {
            if(dto is IDTOArchive dtoArchive)
            {
                dtoArchive.IsArchived = true;
                Save(dto);
                return true;
            }
            else
            {
                throw new Exception("DTO does not implement IDTOArchive");
            }
        }

        public void ArchiveAll(List<T> dtos)
        {

            dtos.ForEach(dto =>
            {
                if (dto is IDTOArchive dtoArchive)
                {
                    dtoArchive.IsArchived = true;
                }
                else
                {
                    throw new Exception("DTOs do not implement IDTOArchive");
                }
            });
            SaveAll(dtos);
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
