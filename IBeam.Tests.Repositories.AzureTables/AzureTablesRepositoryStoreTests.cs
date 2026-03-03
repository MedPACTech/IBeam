using Azure;
using Azure.Data.Tables;
using IBeam.Repositories.Abstractions;
using IBeam.Repositories.AzureTables;
using IBeam.Repositories.Core;
using Microsoft.Extensions.Options;

namespace IBeam.Tests.Repositories.AzureTables;

[TestClass]
public sealed class AzureTablesRepositoryStoreTests
{
    private const string AzuriteConnectionString = "UseDevelopmentStorage=true";
    private static bool _azuriteChecked;
    private static bool _azuriteAvailable;

    [TestMethod]
    public async Task AddAndGetById_EnvelopeModel_RoundTripsEntity()
    {
        await EnsureAzuriteAvailableAsync();

        var store = CreateStore<TestDocEntity>(
            AzureTableStorageModel.Envelope,
            AzureTablePartitionKeyStrategies.Global<TestDocEntity>("global"));

        var entity = new TestDocEntity
        {
            Id = Guid.NewGuid(),
            Name = "alpha",
            Category = "news",
            IsDeleted = false
        };

        await store.AddAsync(null, entity);
        var loaded = await store.GetByIdAsync(null, entity.Id);

        Assert.IsNotNull(loaded);
        Assert.AreEqual(entity.Id, loaded!.Id);
        Assert.AreEqual("alpha", loaded.Name);
        Assert.AreEqual("news", loaded.Category);
    }

    [TestMethod]
    public async Task AddThenUpdate_EntityColumnsModel_UpdatesEntity()
    {
        await EnsureAzuriteAvailableAsync();

        var store = CreateStore<TestDocEntity>(
            AzureTableStorageModel.EntityColumns,
            AzureTablePartitionKeyStrategies.Global<TestDocEntity>("global"));

        var entity = new TestDocEntity
        {
            Id = Guid.NewGuid(),
            Name = "before",
            Category = "ops",
            IsDeleted = false
        };

        await store.AddAsync(null, entity);
        entity.Name = "after";
        entity.Category = "audit";
        await store.UpdateAsync(null, entity);

        var loaded = await store.GetByIdAsync(null, entity.Id);
        Assert.IsNotNull(loaded);
        Assert.AreEqual("after", loaded!.Name);
        Assert.AreEqual("audit", loaded.Category);
    }

    [TestMethod]
    public async Task UpsertAll_TenantHashBucketStrategy_GetAllByTenantReturnsRows()
    {
        await EnsureAzuriteAvailableAsync();

        var tenantId = Guid.NewGuid();
        var store = CreateStore<TestTenantEntity>(
            AzureTableStorageModel.Envelope,
            AzureTablePartitionKeyStrategies.TenantHashBucket<TestTenantEntity>(8));

        var rows = new List<TestTenantEntity>
        {
            new() { Id = Guid.NewGuid(), TenantId = tenantId, Name = "a", IsDeleted = false },
            new() { Id = Guid.NewGuid(), TenantId = tenantId, Name = "b", IsDeleted = false },
            new() { Id = Guid.NewGuid(), TenantId = tenantId, Name = "c", IsDeleted = false }
        };

        await store.UpsertAllAsync(tenantId, rows);
        var loaded = await store.GetAllAsync(tenantId);

        Assert.AreEqual(3, loaded.Count);
    }

    [TestMethod]
    public async Task GetByPartitionPagedAsync_ReturnsResultsAndContinuationToken()
    {
        await EnsureAzuriteAvailableAsync();

        var store = CreateStore<TestDocEntity>(
            AzureTableStorageModel.EntityColumns,
            AzureTablePartitionKeyStrategies.Global<TestDocEntity>("global"));

        var rows = new List<TestDocEntity>
        {
            new() { Id = Guid.NewGuid(), Name = "a", Category = "n", IsDeleted = false },
            new() { Id = Guid.NewGuid(), Name = "b", Category = "n", IsDeleted = false },
            new() { Id = Guid.NewGuid(), Name = "c", Category = "n", IsDeleted = false }
        };

        await store.UpsertAllAsync(null, rows);

        var page1 = await store.GetByPartitionPagedAsync("global", pageSize: 2);
        Assert.AreEqual(2, page1.Results.Count());
        Assert.IsFalse(string.IsNullOrWhiteSpace(page1.ContinuationToken));

        var page2 = await store.GetByPartitionPagedAsync("global", pageSize: 2, continuationToken: page1.ContinuationToken);
        Assert.AreEqual(1, page2.Results.Count());
    }

    [TestMethod]
    public async Task QueryAsync_FiltersSoftDeletedAndAppliesPredicate()
    {
        await EnsureAzuriteAvailableAsync();

        var store = CreateStore<TestDocEntity>(
            AzureTableStorageModel.Envelope,
            AzureTablePartitionKeyStrategies.Global<TestDocEntity>("global"));

        await store.UpsertAllAsync(null, new List<TestDocEntity>
        {
            new() { Id = Guid.NewGuid(), Name = "one", Category = "news", IsDeleted = false },
            new() { Id = Guid.NewGuid(), Name = "two", Category = "news", IsDeleted = true  },
            new() { Id = Guid.NewGuid(), Name = "three", Category = "ops", IsDeleted = false }
        });

        var results = new List<TestDocEntity>();
        await foreach (var item in store.QueryAsync(x => x.Category == "news"))
            results.Add(item);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("one", results[0].Name);
    }

    [TestMethod]
    public async Task AddAsync_WhenDuplicateRow_ThrowsRepositoryStoreException()
    {
        await EnsureAzuriteAvailableAsync();

        var store = CreateStore<TestDocEntity>(
            AzureTableStorageModel.Envelope,
            AzureTablePartitionKeyStrategies.Global<TestDocEntity>("global"));

        var entity = new TestDocEntity
        {
            Id = Guid.NewGuid(),
            Name = "dup",
            Category = "news",
            IsDeleted = false
        };

        await store.AddAsync(null, entity);

        await AssertThrowsAsync<RepositoryStoreException>(() =>
            store.AddAsync(null, entity));
    }

    [TestMethod]
    public async Task UpdateAsync_WithMismatchedEtag_ThrowsRepositoryStoreException()
    {
        await EnsureAzuriteAvailableAsync();

        var store = CreateStore<TestDocEntity>(
            AzureTableStorageModel.EntityColumns,
            AzureTablePartitionKeyStrategies.Global<TestDocEntity>("global"));

        var entity = new TestDocEntity
        {
            Id = Guid.NewGuid(),
            Name = "first",
            Category = "cat",
            IsDeleted = false
        };

        await store.AddAsync(null, entity);
        entity.Name = "second";

        await AssertThrowsAsync<RepositoryStoreException>(() =>
            store.UpdateAsync(null, entity, eTag: new ETag("\"bad-etag\"")));
    }

    [TestMethod]
    public async Task HardDeleteAllAsync_WithUnknownIdCandidates_StillDeletesViaFallback()
    {
        await EnsureAzuriteAvailableAsync();

        var strategy = AzureTablePartitionKeyStrategies.Create<TestDocEntity>(
            writePartition: (_, _) => "global",
            idCandidates: (_, _) => null,
            getAllPartitions: _ => new[] { "global" });

        var store = CreateStore(AzureTableStorageModel.Envelope, strategy);

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        await store.UpsertAllAsync(null, new List<TestDocEntity>
        {
            new() { Id = id1, Name = "a", Category = "x", IsDeleted = false },
            new() { Id = id2, Name = "b", Category = "x", IsDeleted = false }
        });

        await store.HardDeleteAllAsync(null, new[] { id1, id2 });

        var e1 = await store.GetByIdAsync(null, id1);
        var e2 = await store.GetByIdAsync(null, id2);
        Assert.IsNull(e1);
        Assert.IsNull(e2);
    }

    [TestMethod]
    public async Task CustomPartitionStrategy_TenantUserStyle_RoundTrips()
    {
        await EnsureAzuriteAvailableAsync();

        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var strategy = AzureTablePartitionKeyStrategies.Create<TestTenantUserEntity>(
            writePartition: (_, e) => $"{e.TenantId:N}|{e.UserId:N}",
            idCandidates: (_, id) => new[] { $"{tenantId:N}|{userId:N}" },
            getAllPartitions: _ => new[] { $"{tenantId:N}|{userId:N}" });

        var store = CreateStore(AzureTableStorageModel.EntityColumns, strategy);

        var row = new TestTenantUserEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            Name = "message",
            IsDeleted = false
        };

        await store.AddAsync(tenantId, row);
        var loaded = await store.GetByIdAsync(tenantId, row.Id);
        Assert.IsNotNull(loaded);
        Assert.AreEqual("message", loaded!.Name);
    }

    [TestMethod]
    public async Task QueryAsync_InEntityColumnsMode_MapsCommonTypes()
    {
        await EnsureAzuriteAvailableAsync();

        var store = CreateStore<TestTypedEntity>(
            AzureTableStorageModel.EntityColumns,
            AzureTablePartitionKeyStrategies.Global<TestTypedEntity>("global"));

        var now = DateTimeOffset.UtcNow;
        var entity = new TestTypedEntity
        {
            Id = Guid.NewGuid(),
            IsDeleted = false,
            IsActive = true,
            Count = 7,
            Score = 4.5,
            When = now,
            State = TestState.Done,
            OptionalNumber = 42,
            Note = "typed"
        };

        await store.AddAsync(null, entity);

        var results = new List<TestTypedEntity>();
        await foreach (var item in store.QueryAsync(x => x.Note == "typed"))
            results.Add(item);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(true, results[0].IsActive);
        Assert.AreEqual(7, results[0].Count);
        Assert.AreEqual(TestState.Done, results[0].State);
        Assert.AreEqual(42, results[0].OptionalNumber);
    }

    [TestMethod]
    public async Task GetByKeysAsync_WithCompositeMapping_RoundTripsAndDeletes()
    {
        await EnsureAzuriteAvailableAsync();

        var tenantId = Guid.NewGuid();
        var patientId = Guid.NewGuid();

        var mapping = new AzureEntityMappingOptions<TestCompositeEntity>
        {
            TableName = "PatientClinicalNotes",
            WriteKey = (_, e) => new AzureEntityKey
            {
                PartitionKey = $"TENANT={e.TenantId:D}|PATIENT={e.PatientId:D}",
                RowKey = e.Id.ToString("N")
            },
            EnableIdLocator = false
        };

        var store = CreateStore(
            AzureTableStorageModel.EntityColumns,
            AzureTablePartitionKeyStrategies.Global<TestCompositeEntity>("unused"),
            mapping,
            new InMemoryEntityLocator());

        var row = new TestCompositeEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PatientId = patientId,
            Name = "note-1",
            IsDeleted = false
        };

        var key = mapping.WriteKey(null, row);
        await store.AddAsync(tenantId, row);

        var loaded = await store.GetByKeysAsync(key.PartitionKey, key.RowKey);
        Assert.IsNotNull(loaded);
        Assert.AreEqual("note-1", loaded!.Name);

        await store.DeleteByKeysAsync(key.PartitionKey, key.RowKey);

        var deleted = await store.GetByKeysAsync(key.PartitionKey, key.RowKey);
        Assert.IsNull(deleted);
    }

    [TestMethod]
    public async Task GetByIdAsync_WithLocatorEnabled_ResolvesCompositePartitionWithoutScan()
    {
        await EnsureAzuriteAvailableAsync();

        var locator = new InMemoryEntityLocator();
        var mapping = new AzureEntityMappingOptions<TestCompositeEntity>
        {
            TableName = "PatientClinicalNotes",
            WriteKey = (_, e) => new AzureEntityKey
            {
                PartitionKey = $"TENANT={e.TenantId:D}|PATIENT={e.PatientId:D}",
                RowKey = e.Id.ToString("N")
            },
            CandidatePartitionsForId = static (_, _) => Array.Empty<string>(),
            EnableIdLocator = true
        };

        var store = CreateStore(
            AzureTableStorageModel.Envelope,
            AzureTablePartitionKeyStrategies.Global<TestCompositeEntity>("unused"),
            mapping,
            locator);

        var row = new TestCompositeEntity
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            Name = "note-2",
            IsDeleted = false
        };

        await store.UpsertAsync(row.TenantId, row);

        var loaded = await store.GetByIdAsync(row.TenantId, row.Id);
        Assert.IsNotNull(loaded);
        Assert.AreEqual("note-2", loaded!.Name);
    }

    [TestMethod]
    public async Task GetByPartitionPagedAsync_WithInvalidPageSize_Throws()
    {
        await EnsureAzuriteAvailableAsync();

        var store = CreateStore<TestDocEntity>(
            AzureTableStorageModel.Envelope,
            AzureTablePartitionKeyStrategies.Global<TestDocEntity>("global"));

        await AssertThrowsAsync<ArgumentOutOfRangeException>(() =>
            store.GetByPartitionPagedAsync("global", pageSize: 0));
    }

    private static IAzureTablesRepositoryStore<T> CreateStore<T>(
        AzureTableStorageModel storageModel,
        IAzureTablePartitionKeyStrategy<T> strategy)
        where T : class, IEntity
    {
        var options = Options.Create(new AzureTablesOptions
        {
            ConnectionString = AzuriteConnectionString,
            CreateTablesIfNotExists = true,
            StorageModel = storageModel,
            TableNamePrefix = $"t{Guid.NewGuid():N}".Substring(0, 18)
        });

        return new AzureTablesRepositoryStore<T>(options, strategy);
    }

    private static IAzureTablesRepositoryStore<T> CreateStore<T>(
        AzureTableStorageModel storageModel,
        IAzureTablePartitionKeyStrategy<T> strategy,
        AzureEntityMappingOptions<T> mapping,
        IEntityLocator locator)
        where T : class, IEntity
    {
        var options = Options.Create(new AzureTablesOptions
        {
            ConnectionString = AzuriteConnectionString,
            CreateTablesIfNotExists = true,
            StorageModel = storageModel,
            TableNamePrefix = $"t{Guid.NewGuid():N}".Substring(0, 18)
        });

        return new AzureTablesRepositoryStore<T>(options, strategy, mapping, locator);
    }

    private static async Task EnsureAzuriteAvailableAsync()
    {
        if (_azuriteChecked)
        {
            if (!_azuriteAvailable)
                Assert.Inconclusive("Azurite is not reachable. Start Azurite and re-run tests.");
            return;
        }

        try
        {
            var service = new TableServiceClient(AzuriteConnectionString);
            var probe = service.GetTableClient($"probe{Guid.NewGuid():N}".Substring(0, 20));
            await probe.CreateIfNotExistsAsync();
            await probe.DeleteAsync();
            _azuriteAvailable = true;
        }
        catch (Exception ex) when (
            ex is RequestFailedException ||
            ex is InvalidOperationException ||
            ex is HttpRequestException ||
            ex is AggregateException)
        {
            _azuriteAvailable = false;
        }
        finally
        {
            _azuriteChecked = true;
        }

        if (!_azuriteAvailable)
            Assert.Inconclusive("Azurite is not reachable. Start Azurite and re-run tests.");
    }

    private static async Task<TException> AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
            Assert.Fail($"Expected exception {typeof(TException).Name} was not thrown.");
            throw new InvalidOperationException("Unreachable");
        }
        catch (TException ex)
        {
            return ex;
        }
    }

    private sealed class TestDocEntity : IEntity
    {
        public Guid Id { get; set; }
        public bool IsDeleted { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
    }

    private sealed class TestTenantEntity : IEntity, ITenantEntity
    {
        public Guid Id { get; set; }
        public bool IsDeleted { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class TestTenantUserEntity : IEntity, ITenantEntity
    {
        public Guid Id { get; set; }
        public bool IsDeleted { get; set; }
        public Guid TenantId { get; set; }
        public Guid UserId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class TestTypedEntity : IEntity
    {
        public Guid Id { get; set; }
        public bool IsDeleted { get; set; }
        public bool IsActive { get; set; }
        public int Count { get; set; }
        public double Score { get; set; }
        public DateTimeOffset When { get; set; }
        public TestState State { get; set; }
        public int? OptionalNumber { get; set; }
        public string Note { get; set; } = string.Empty;
    }

    private sealed class TestCompositeEntity : IEntity, ITenantEntity
    {
        public Guid Id { get; set; }
        public bool IsDeleted { get; set; }
        public Guid TenantId { get; set; }
        public Guid PatientId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private enum TestState
    {
        New = 0,
        Done = 1
    }
}
