using IBeam.Repositories.Abstractions;
using IBeam.Repositories.Core;
using Microsoft.Extensions.Caching.Memory;

namespace IBeam.Repositories.OrmLite;

public class OrmLiteRepository<T> : BaseRepositoryAsync<T>
    where T : class, IEntity
{
    public OrmLiteRepository(
        IRepositoryStore<T> store,
        IMemoryCache cache,
        ITenantContext tenantContext,
        RepositoryOptions options)
        : base(store, cache, tenantContext, options)
    { }
}
