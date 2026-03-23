using IBeam.Repositories.Abstractions;
using IBeam.Repositories.Core;
using IBeam.Services.Abstractions;
using IBeam.Services.Core;
using Moq;

namespace IBeam.Tests.Services;

[TestClass]
public sealed class BaseServiceAsyncTests
{
    [TestMethod]
    public async Task GetByIdAsync_WhenRepositoryReturnsNull_ThrowsKeyNotFound()
    {
        var repo = new Mock<IBaseRepositoryAsync<TestEntity>>(MockBehavior.Strict);
        var mapper = new TestMapper();
        var service = new TestAsyncService(repo.Object, mapper);

        var id = Guid.NewGuid();
        repo.Setup(x => x.GetByIdAsync(id, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TestEntity?)null);

        await AssertThrowsAsync<KeyNotFoundException>(() => service.GetByIdAsync(id));
    }

    [TestMethod]
    public async Task SaveAllAsync_AuditsCreateAndUpdate_AndRunsPostHooks()
    {
        var repo = new Mock<IBaseRepositoryAsync<TestEntity>>(MockBehavior.Strict);
        var mapper = new TestMapper();
        var audit = new Mock<IEntityAuditServiceAsync<TestEntity>>(MockBehavior.Strict);
        var service = new TestAsyncService(repo.Object, mapper, audit.Object);

        var create = new TestModel { Id = Guid.Empty, Name = "create" };
        var updateId = Guid.NewGuid();
        var update = new TestModel { Id = updateId, Name = "update" };
        repo.Setup(x => x.GetByIdsAsync(
                It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == updateId),
                true,
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TestEntity> { new() { Id = updateId, Name = "existing" } });

        repo.Setup(x => x.SaveAllAsync(It.IsAny<IReadOnlyList<TestEntity>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TestEntity> entities, CancellationToken _) =>
                entities
                    .Select(e => e.Id == Guid.Empty ? e with { Id = Guid.NewGuid() } : e)
                    .ToList());

        audit.Setup(x => x.LogCreateAsync(It.IsAny<TestEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        audit.Setup(x => x.LogUpdateAsync(It.IsAny<TestEntity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        audit.Setup(x => x.LogAuditAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = (await service.SaveAllAsync(new[] { create, update })).ToList();

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(2, service.PostSaveCalls);
        Assert.AreEqual(1, service.PreSaveCreateCalls);
        Assert.AreEqual(1, service.PreSaveUpdateCalls);
        audit.Verify(x => x.LogCreateAsync(It.IsAny<TestEntity>(), It.IsAny<CancellationToken>()), Times.Once);
        audit.Verify(x => x.LogUpdateAsync(It.IsAny<TestEntity>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ArchiveAllAsync_UsesRepositoryBulkMethod_WithDistinctIds()
    {
        var repo = new Mock<IBaseRepositoryAsync<TestEntity>>(MockBehavior.Strict);
        var mapper = new TestMapper();
        var service = new TestAsyncService(repo.Object, mapper);

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        repo.Setup(x => x.ArchiveAllAsync(It.Is<IReadOnlyList<Guid>>(ids =>
                ids.Count == 2 &&
                ids.Contains(id1) &&
                ids.Contains(id2)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await service.ArchiveAllAsync(new[]
        {
            new TestModel { Id = id1, Name = "a" },
            new TestModel { Id = id1, Name = "a-dup" },
            new TestModel { Id = id2, Name = "b" },
            new TestModel { Id = Guid.Empty, Name = "empty" }
        });

        Assert.AreEqual(2, service.PreArchiveCalls);
        Assert.AreEqual(2, service.PostArchiveCalls);
        repo.Verify(x => x.ArchiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task UnarchiveAllAsync_UsesRepositoryBulkMethod_WithDistinctIds()
    {
        var repo = new Mock<IBaseRepositoryAsync<TestEntity>>(MockBehavior.Strict);
        var mapper = new TestMapper();
        var service = new TestAsyncService(repo.Object, mapper);

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        repo.Setup(x => x.UnarchiveAllAsync(It.Is<IReadOnlyList<Guid>>(ids =>
                ids.Count == 2 &&
                ids.Contains(id1) &&
                ids.Contains(id2)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await service.UnarchiveAllAsync(new[]
        {
            new TestModel { Id = id1, Name = "a" },
            new TestModel { Id = id2, Name = "b" },
            new TestModel { Id = id2, Name = "b-dup" }
        });

        Assert.AreEqual(2, service.PreUnarchiveCalls);
        Assert.AreEqual(2, service.PostUnarchiveCalls);
        repo.Verify(x => x.UnarchiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    [TestMethod]
    public async Task SaveAllAsync_WhenMethodNotAllowed_ThrowsMethodAccess()
    {
        var service = new LockedAsyncService(
            Mock.Of<IBaseRepositoryAsync<TestEntity>>(),
            new TestMapper());

        await AssertThrowsAsync<MethodAccessException>(() =>
            service.SaveAllAsync(new[] { new TestModel { Id = Guid.Empty, Name = "x" } }));
    }

    [TestMethod]
    public async Task SaveAllAsync_WhenRepositoryThrows_PassesThroughRepositoryException()
    {
        var repo = new Mock<IBaseRepositoryAsync<TestEntity>>(MockBehavior.Strict);
        var service = new TestAsyncService(repo.Object, new TestMapper());
        var id = Guid.NewGuid();

        repo.Setup(x => x.GetByIdsAsync(
                It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == id),
                true,
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TestEntity>());
        repo.Setup(x => x.SaveAllAsync(It.IsAny<IReadOnlyList<TestEntity>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RepositoryException("TestRepo", "SaveAllAsync", "boom"));

        await AssertThrowsAsync<RepositoryException>(() =>
            service.SaveAllAsync(new[] { new TestModel { Id = id, Name = "x" } }));
    }

    [TestMethod]
    public async Task ArchiveAllAsync_WhenEmptyInput_DoesNotCallRepository()
    {
        var repo = new Mock<IBaseRepositoryAsync<TestEntity>>(MockBehavior.Strict);
        var service = new TestAsyncService(repo.Object, new TestMapper());

        await service.ArchiveAllAsync(Array.Empty<TestModel>());
        repo.Verify(x => x.ArchiveAllAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task DeleteAsync_LogsTypedDeleteAudit_WhenEntityExists()
    {
        var id = Guid.NewGuid();
        var entity = new TestEntity { Id = id, Name = "to-delete" };

        var repo = new Mock<IBaseRepositoryAsync<TestEntity>>(MockBehavior.Strict);
        var mapper = new TestMapper();
        var audit = new Mock<IEntityAuditServiceAsync<TestEntity>>(MockBehavior.Strict);
        var service = new DeleteEnabledAsyncService(repo.Object, mapper, audit.Object);

        repo.Setup(x => x.GetByIdAsync(id, true, true, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
        repo.Setup(x => x.DeleteAsync(id, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        audit.Setup(x => x.LogDeleteAsync(It.Is<TestEntity>(e => e.Id == id), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await service.DeleteAsync(id);

        audit.Verify(x => x.LogDeleteAsync(It.IsAny<TestEntity>(), It.IsAny<CancellationToken>()), Times.Once);
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
    private sealed class LockedAsyncService : BaseServiceAsync<TestEntity, TestModel>
    {
        public LockedAsyncService(
            IBaseRepositoryAsync<TestEntity> repository,
            IModelMapper<TestEntity, TestModel> mapper)
            : base(repository, mapper)
        {
        }
    }

    private sealed class DeleteEnabledAsyncService : BaseServiceAsync<TestEntity, TestModel>
    {
        public DeleteEnabledAsyncService(
            IBaseRepositoryAsync<TestEntity> repository,
            IModelMapper<TestEntity, TestModel> mapper,
            IAuditServiceAsync audit)
            : base(repository, mapper, audit)
        {
            AllowDelete = true;
        }
    }

    private sealed class TestAsyncService : BaseServiceAsync<TestEntity, TestModel>
    {
        public int PreSaveCreateCalls { get; private set; }
        public int PreSaveUpdateCalls { get; private set; }
        public int PostSaveCalls { get; private set; }
        public int PreArchiveCalls { get; private set; }
        public int PostArchiveCalls { get; private set; }
        public int PreUnarchiveCalls { get; private set; }
        public int PostUnarchiveCalls { get; private set; }

        public TestAsyncService(
            IBaseRepositoryAsync<TestEntity> repository,
            IModelMapper<TestEntity, TestModel> mapper,
            IAuditServiceAsync? audit = null)
            : base(repository, mapper, audit)
        {
            AllowGetById = true;
            AllowSaveAll = true;
            AllowArchive = true;
            AllowUnarchive = true;
        }

        protected override Task PreSaveAsync(TestModel model, bool isUpdate, CancellationToken ct)
        {
            if (isUpdate) PreSaveUpdateCalls++;
            else PreSaveCreateCalls++;
            return Task.CompletedTask;
        }

        protected override Task PostSaveAsync(TestModel model, bool isUpdate, CancellationToken ct)
        {
            PostSaveCalls++;
            return Task.CompletedTask;
        }

        protected override Task PreArchiveAsync(Guid id, CancellationToken ct)
        {
            PreArchiveCalls++;
            return Task.CompletedTask;
        }

        protected override Task PostArchiveAsync(Guid id, CancellationToken ct)
        {
            PostArchiveCalls++;
            return Task.CompletedTask;
        }

        protected override Task PreUnarchiveAsync(Guid id, CancellationToken ct)
        {
            PreUnarchiveCalls++;
            return Task.CompletedTask;
        }

        protected override Task PostUnarchiveAsync(Guid id, CancellationToken ct)
        {
            PostUnarchiveCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class TestMapper : IModelMapper<TestEntity, TestModel>
    {
        public TestEntity ToEntity(TestModel model) => new()
        {
            Id = model.Id,
            IsDeleted = model.IsDeleted,
            Name = model.Name
        };

        public TestModel ToModel(TestEntity entity) => new()
        {
            Id = entity.Id,
            IsDeleted = entity.IsDeleted,
            Name = entity.Name
        };

        public IEnumerable<TestEntity> ToEntity(IEnumerable<TestModel> models) => models.Select(ToEntity);
        public IEnumerable<TestModel> ToModel(IEnumerable<TestEntity> entities) => entities.Select(ToModel);
    }

    public sealed record TestEntity : IEntity
    {
        public Guid Id { get; set; }
        public bool IsDeleted { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public sealed class TestModel
    {
        public Guid Id { get; set; }
        public bool IsDeleted { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}




