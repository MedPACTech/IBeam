using IBeam.Repositories.Abstractions;
using IBeam.Repositories.Core;
using Microsoft.Extensions.Caching.Memory;

namespace IBeam.Tests.Repositories;

[TestClass]
public sealed class BaseRepositoryCacheTests
{
    [TestMethod]
    public async Task GetAllAsync_CachedResultExpiresAndRequeriesStore()
    {
        var store = new CountingStore();
        var repo = CreateRepo(store, new RepositoryOptions { CacheDuration = TimeSpan.FromMilliseconds(100) });

        await repo.GetAllAsync();
        await repo.GetAllAsync();
        Assert.AreEqual(1, store.GetAllCalls, "Second call within the TTL should be served from cache.");

        await Task.Delay(400);

        await repo.GetAllAsync();
        Assert.AreEqual(2, store.GetAllCalls, "Call after the TTL should re-query the store.");
    }

    [TestMethod]
    public async Task GetAllAsync_WithNullCacheDuration_CachesUntilInvalidated()
    {
        var store = new CountingStore();
        var repo = CreateRepo(store, new RepositoryOptions { CacheDuration = null });

        await repo.GetAllAsync();
        await repo.GetAllAsync();
        Assert.AreEqual(1, store.GetAllCalls);

        await repo.SaveAsync(new TestEntity { Id = Guid.NewGuid() });
        await repo.GetAllAsync();
        Assert.AreEqual(2, store.GetAllCalls, "A write through the repository should invalidate the cache.");
    }

    [TestMethod]
    public async Task GetAllAsync_PerRepositoryCacheDurationOverrideWins()
    {
        var store = new CountingStore();
        var repo = new ShortLivedCacheRepository(store, new MemoryCache(new MemoryCacheOptions()),
            new NoTenantContext(), new RepositoryOptions { CacheDuration = TimeSpan.FromHours(1) });

        await repo.GetAllAsync();
        await Task.Delay(400);
        await repo.GetAllAsync();

        Assert.AreEqual(2, store.GetAllCalls, "The repository-level override should shorten the TTL.");
    }

    [TestMethod]
    public void RepositoryOptions_CacheDurationDefaultsToSafetyNetTtl()
    {
        Assert.AreEqual(TimeSpan.FromMinutes(15), new RepositoryOptions().CacheDuration);
    }

    private static TestRepository CreateRepo(CountingStore store, RepositoryOptions options) =>
        new(store, new MemoryCache(new MemoryCacheOptions()), new NoTenantContext(), options);

    private sealed class TestEntity : IEntity
    {
        public Guid Id { get; set; }
        public bool IsDeleted { get; set; }
    }

    private class TestRepository : BaseRepositoryAsync<TestEntity>
    {
        public TestRepository(IRepositoryStore<TestEntity> store, IMemoryCache cache, ITenantContext tenants, RepositoryOptions options)
            : base(store, cache, tenants, options)
        {
        }
    }

    private sealed class ShortLivedCacheRepository : TestRepository
    {
        public ShortLivedCacheRepository(IRepositoryStore<TestEntity> store, IMemoryCache cache, ITenantContext tenants, RepositoryOptions options)
            : base(store, cache, tenants, options)
        {
        }

        protected override TimeSpan? CacheDuration => TimeSpan.FromMilliseconds(100);
    }

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid? TenantId => null;
        public bool IsTenantIdSet() => false;
    }

    private sealed class CountingStore : IRepositoryStore<TestEntity>
    {
        public int GetAllCalls;

        public Task<TestEntity?> GetByIdAsync(Guid? tenantId, Guid id, CancellationToken ct = default) =>
            Task.FromResult<TestEntity?>(null);

        public Task<IReadOnlyList<TestEntity>> GetByIdsAsync(Guid? tenantId, IReadOnlyList<Guid> ids, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TestEntity>>([]);

        public Task<IReadOnlyList<TestEntity>> GetAllAsync(Guid? tenantId, CancellationToken ct = default)
        {
            GetAllCalls++;
            return Task.FromResult<IReadOnlyList<TestEntity>>([new TestEntity { Id = Guid.NewGuid() }]);
        }

        public Task<TestEntity> UpsertAsync(Guid? tenantId, TestEntity entity, CancellationToken ct = default) =>
            Task.FromResult(entity);

        public Task<IReadOnlyList<TestEntity>> UpsertAllAsync(Guid? tenantId, IReadOnlyList<TestEntity> entities, CancellationToken ct = default) =>
            Task.FromResult(entities);

        public Task HardDeleteAsync(Guid? tenantId, Guid id, CancellationToken ct = default) => Task.CompletedTask;

        public Task HardDeleteAllAsync(Guid? tenantId, IReadOnlyList<Guid> ids, CancellationToken ct = default) => Task.CompletedTask;
    }
}
