using IBeam.Repositories.Abstractions;
using IBeam.Repositories.Core;
using Microsoft.Extensions.Caching.Memory;

namespace IBeam.Repositories.AzureTables;

public sealed class AzureTablesRepositoryAsync<T> : AzureTablesRepositoryBase<T>
    where T : class, IEntity
{
    public AzureTablesRepositoryAsync(
        IAzureTablesRepositoryStore<T> store,
        IMemoryCache cache,
        ITenantContext tenantContext,
        RepositoryOptions options)
        : base(store, cache, tenantContext, options)
    { }
}
