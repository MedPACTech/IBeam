using System.Data;
using IBeam.DataModels.System;
using ServiceStack.OrmLite;

namespace IBeam.Repositories.Interfaces
{
    public interface IBaseRepository<T> where T : class, IDTO
    {
        string RepositoryName { get; }
        string RepositoryCacheName { get; }
        bool IsArchivable { get; }
        bool IsSoftDeleteDisabled { get; }
        bool IsTenantSpecific { get; }
        bool EnableCache { get; set; }
        bool IdGeneratedByRepository { get; }


        List<T> GetAll(bool withArchived = false);
        List<T> GetByIds(List<Guid> ids);
        T GetById(Guid id);

        List<T> QueryWhere(
            Func<SqlExpression<T>, SqlExpression<T>> expressionBuilder,
            bool includeArchived = false,
            bool includeDeleted = false);

        T Save(T dto);
        List<T> SaveAll(List<T> dtos);

        bool Archive(T dto);
        bool ArchiveAll(List<T> dtos);

        bool Unarchive(T dto);
        bool UnarchiveAll(List<T> dtos);

        void Delete(T dto);
        void DeleteById(Guid id);
    }
}
