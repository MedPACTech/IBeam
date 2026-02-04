using IBeam.Repositories.Abstractions;
using IBeam.Repositories.Core;
using Microsoft.Extensions.Caching.Memory;

namespace IBeam.Repositories.AzureTables;

public sealed class AzureTablesRepository<T> : BaseRepositoryAsync<T>
    where T : class, IEntity
{
    public AzureTablesRepository(
        IRepositoryStore<T> store,
        IMemoryCache cache,
        ITenantContext tenantContext,
        RepositoryOptions options)
        : base(store, cache, tenantContext, options)
    { }
}
