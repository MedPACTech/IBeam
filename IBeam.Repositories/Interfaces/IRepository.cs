using System;
using System.Collections.Generic;
using IBeam.DataModels;

namespace IBeam.Repositories.Interfaces
{
    public interface IRepository<T> where T : class, IDTO
    {
        string RepositoryName { get; }
        string RepositoryCacheName { get; }
        /// <summary>
        /// Does data object implement .IsArchived
        /// </summary>
        bool IsArchivable { get; }
        /// <summary>
        /// Enables repository to use memory cache on GetALL() requests. 
        /// Use this to access small datasets with few to no writes
        /// </summary>
        bool EnableCache { get; set; }
        /// <summary>
        /// Deletes a single of record in repository. 
        /// This will clear any GetAll() cached records
        /// </summary>
        /// <param name="dto">dto to remove</param>
        void Delete(T dto);
        /// <summary>
        /// Deletes a single of record in repository. 
        /// This will clear any GetAll() cached records
        /// </summary>
        /// <param name="id">id of repository to remove</param>
        void DeleteById(Guid id);
        /// <summary>
        /// Gets all records from repository. 
        /// Turn off Memory Cache when accessing archived records, this will cause an exception
        /// </summary>
        /// <param name="withArchived">returns archived records</param>
        IEnumerable<T> GetAll(bool withArchive = false);
        /// <summary>
        /// Gets a single record from repository by Id. 
        /// </summary>
        /// <param name="id">Id to access</param>
        T GetById(Guid id);
        /// <summary>
        /// Gets all records from repository with matching Ids. 
        /// </summary>
        /// <param name="Ids">List of Ids to searh for</param>
        IEnumerable<T> GetByIds(List<Guid> Ids);
        /// <summary>
        /// Saves a single record to repository. 
        /// This will clear any GetAll() cached records
        /// </summary>
        /// <param name="dto">dto to save</param>
        void Save(T dto);
        /// <summary>
        /// Saves a collection of records to repository. 
        /// This will clear any GetAll() cached records
        /// </summary>
        /// <param name="dtos">dtos to save</param>
        void SaveAll(List<T> dtos);

       
       
    }
}