using IBeam.DataModels.System;
using IBeam.Repositories.Core;
using Microsoft.Extensions.Caching.Memory;

namespace IBeam.Repositories.OrmLite;

public class OrmLiteRepository<T> : RepositoryBase<T>
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
