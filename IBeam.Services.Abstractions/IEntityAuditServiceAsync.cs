using IBeam.Repositories.Abstractions;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace IBeam.Services.Abstractions
{
    public interface IEntityAuditServiceAsync<TEntity> : IAuditServiceAsync
        where TEntity : class, IEntity
    {
        Task LogCreateAsync(TEntity entity, CancellationToken ct = default);
        Task LogUpdateAsync(TEntity entity, CancellationToken ct = default);
        Task LogDeleteAsync(TEntity entity, CancellationToken ct = default);
    }
}
