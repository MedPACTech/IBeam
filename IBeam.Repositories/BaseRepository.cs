using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using IBeam.DataModels;
using IBeam.Repositories.Interfaces;
using ServiceStack.OrmLite;
using ServiceStack.OrmLite.Legacy;
using IBeam.Utilities;
//using ServiceStack;

//todo: abstract out ormlite inject into ormlite Base if possible
namespace IBeam.Repositories
{
    public abstract class BaseRepository<T> : IRepository<T> where T : class, IDTO
    {
        public string RepositoryName { get; }
        public string RepositoryCacheName { get; }
        public bool IsArchivable { get; }
        public bool EnableCache { get; set; }
      
        protected readonly OrmLiteConnectionFactory _dataFactory;
        protected readonly IMemoryCache _memoryCache;
        protected readonly AppSettings _appSettings;

        public BaseRepository(IOptions<AppSettings> appSettings, IMemoryCache memoryCache)
        {
            //todo: config string value from settings as well
            var repositoryType = typeof(T);
            RepositoryName = repositoryType.FullName.Replace("DTO", "Repository");
            RepositoryCacheName = RepositoryName + "Cache";
            IsArchivable = repositoryType.IsAssignableFrom(typeof(IDTOArchive));
            EnableCache = false;

            _appSettings = appSettings.Value;
            var dialectProvider = GetDatabaseType(_appSettings);

            _dataFactory = new OrmLiteConnectionFactory(_appSettings.ConnectionString, dialectProvider);
            _dataFactory.DialectProvider.NamingStrategy = new OrmLiteNamingStrategyBase();
            _memoryCache = memoryCache;
        }

        //todo: look into other ORMs that allow repository run with same interfaces
        private IOrmLiteDialectProvider GetDatabaseType(AppSettings appSettings)
        {
            return appSettings.DatabaseType switch
            {
                "MSSql" => SqlServerDialect.Provider,
                "Postgres" => PostgreSqlDialect.Provider,
                _ => throw new Exception($"Unrecognized database type '{appSettings.DatabaseType}'")
            };
        }

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

        public virtual IEnumerable<T> GetByIds(List<Guid> Ids)
        {
            try
            {
                using IDbConnection db = _dataFactory.OpenDbConnection();
                return db.Select<T> (x => Sql.In(x.Id, Ids));
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "GetByIds", null, Ids);
            }
        }

        public T GetById(Guid id)
        {
            try
            {
                using IDbConnection db = _dataFactory.OpenDbConnection();
                return db.Select<T>(x => x.Id == id).FirstOrDefault();
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "GetById", null, id);
            }
        }

        //todo: configurable to have DB generate ids, also sending ids back as well. 
        public void Save(T dto)
        {
            try
            {
                if (dto.Id == Guid.Empty)
                    throw new Exception("Empty Id is not allowed on Save");

                using IDbConnection db = _dataFactory.OpenDbConnection();
                db.Save(dto);
                ClearCache();
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "Save", dto);
            }
        }

        public void SaveAll(List<T> dtos)
        {
            try
            {
                var emptyIdCount = dtos.Where(x => x.Id == Guid.Empty).Count();

                if (emptyIdCount > 0)
                    throw new Exception("Empty Ids is not allowed on SaveAll");

                using IDbConnection db = _dataFactory.OpenDbConnection();
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

        public void Delete(T dto)
        {
            try
            {
                using IDbConnection db = _dataFactory.OpenDbConnection();
                db.Delete(dto);
                ClearCache();
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "Delete", dto);
            }
        }

        public void DeleteById(Guid id)
        {
            try
            {
                using IDbConnection db = _dataFactory.OpenDbConnection();
                db.DeleteById<T>(id);
                ClearCache();
            }
            catch (Exception ex)
            {
                throw new RepositoryException(ex, RepositoryName, "DeleteById", id);
            }
        }

        private void ClearCache()
        {
            if (EnableCache)
                _memoryCache.Remove(RepositoryCacheName);
        }

        private IEnumerable<T> GetAllCached()
        {
            var results = _memoryCache.Get<IEnumerable<T>>(RepositoryCacheName);

            if (results == null)
            {
                var newResults = GetAllWithArchive(false);
                _memoryCache.Set(RepositoryCacheName, newResults);
                results = newResults;
            }

            return results;
        }

        private IEnumerable<T> GetAllWithArchive(bool withArchived)
        {
            using IDbConnection db = _dataFactory.OpenDbConnection();

            if (IsArchivable && withArchived == false)
            {
                return db.Select<T>().Cast<IDTOArchive>().Where(x => !x.IsArchived).Cast<T>();
            }

            return db.Select<T>();
        }
    }
}
