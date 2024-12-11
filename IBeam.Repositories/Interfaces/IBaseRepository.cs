using IBeam.DataModels.System;
using System;
using System.Collections.Generic;

namespace IBeam.Repositories.Interfaces
{
    public interface IBaseRepository<T> where T : class, IDTO
    {
        string RepositoryName { get; }
        string RepositoryCacheName { get; }
        bool IsArchivable { get; }
        bool IsSoftDeleteDisabled { get; }
        bool IsTenantSpecific { get; }
        Guid? TenantId { get; } 
        bool EnableCache { get; set; }
        bool IdGeneratedByRepository { get; }


        IEnumerable<T> GetAll(bool withArchived = false);
        List<T> GetByIds(List<Guid> ids);
        T GetById(Guid id);

        T Save(T dto);
        void SaveAll(List<T> dtos);

        bool Archive(T dto);
        void ArchiveAll(List<T> dtos);

        void Delete(T dto);
        void DeleteById(Guid id);
    }
}
