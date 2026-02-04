using IBeam.Repositories.Abstractions;
using System.Security.Principal;

namespace IBeam.Services.Abstractions
{
    public interface IEntityAuditService<TEntity> : IAuditService
        where TEntity : class, IEntity
    {
        void LogCreate(TEntity entity);
        void LogUpdate(TEntity entity);
        void LogDelete(TEntity entity);
    }
}
