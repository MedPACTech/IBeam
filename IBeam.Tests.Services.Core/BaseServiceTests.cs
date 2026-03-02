using IBeam.Repositories.Abstractions;
using IBeam.Repositories.Core;
using IBeam.Services.Abstractions;
using IBeam.Services.Core;
using Moq;

namespace IBeam.Tests.Services.Core;

[TestClass]
public sealed class BaseServiceTests
{
    [TestMethod]
    public void GetById_WhenRepositoryReturnsNull_ThrowsKeyNotFound()
    {
        var repo = new Mock<IBaseRepository<TestEntity>>(MockBehavior.Strict);
        var mapper = new TestMapper();
        var service = new TestSyncService(repo.Object, mapper);

        var id = Guid.NewGuid();
        repo.Setup(x => x.GetById(id, false, false)).Returns((TestEntity?)null);

        AssertThrows<KeyNotFoundException>(() => service.GetById(id));
    }

    [TestMethod]
    public void SaveAll_AuditsCreateAndUpdate_ByEntityState()
    {
        var repo = new Mock<IBaseRepository<TestEntity>>(MockBehavior.Strict);
        var mapper = new TestMapper();
        var audit = new Mock<IEntityAuditService<TestEntity>>(MockBehavior.Strict);
        var service = new TestSyncService(repo.Object, mapper, audit.Object);

        var createModel = new TestModel { Id = Guid.Empty, Name = "create" };
        var updateId = Guid.NewGuid();
        var updateModel = new TestModel { Id = updateId, Name = "update" };

        repo.Setup(x => x.SaveAll(It.IsAny<IReadOnlyList<TestEntity>>()))
            .Returns<IReadOnlyList<TestEntity>>(entities =>
            {
                return entities
                    .Select(e => e.Id == Guid.Empty ? e with { Id = Guid.NewGuid() } : e)
                    .ToList();
            });

        audit.Setup(x => x.LogCreate(It.IsAny<TestEntity>()));
        audit.Setup(x => x.LogUpdate(It.IsAny<TestEntity>()));
        audit.Setup(x => x.LogAudit(It.IsAny<object>()));

        var saved = service.SaveAll(new[] { createModel, updateModel }).ToList();

        Assert.AreEqual(2, saved.Count);
        Assert.AreNotEqual(Guid.Empty, saved[0].Id);
        Assert.AreEqual(updateId, saved[1].Id);
        Assert.AreEqual(2, service.PostSaveCalls);
        Assert.AreEqual(1, service.PreSaveCreateCalls);
        Assert.AreEqual(1, service.PreSaveUpdateCalls);
        audit.Verify(x => x.LogCreate(It.IsAny<TestEntity>()), Times.Once);
        audit.Verify(x => x.LogUpdate(It.IsAny<TestEntity>()), Times.Once);
    }

    [TestMethod]
    public void ArchiveAll_UsesRepositoryBulkMethod_WithDistinctIds()
    {
        var repo = new Mock<IBaseRepository<TestEntity>>(MockBehavior.Strict);
        var mapper = new TestMapper();
        var service = new TestSyncService(repo.Object, mapper);

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        repo.Setup(x => x.ArchiveAll(It.Is<IReadOnlyList<Guid>>(ids =>
                ids.Count == 2 &&
                ids.Contains(id1) &&
                ids.Contains(id2))))
            .Returns(true);

        service.ArchiveAll(new[]
        {
            new TestModel { Id = id1, Name = "a" },
            new TestModel { Id = id1, Name = "a-dup" },
            new TestModel { Id = id2, Name = "b" },
            new TestModel { Id = Guid.Empty, Name = "empty" }
        });

        Assert.AreEqual(2, service.PreArchiveCalls);
        Assert.AreEqual(2, service.PostArchiveCalls);
        repo.Verify(x => x.Archive(It.IsAny<Guid>()), Times.Never);
    }

    [TestMethod]
    public void UnarchiveAll_UsesRepositoryBulkMethod_WithDistinctIds()
    {
        var repo = new Mock<IBaseRepository<TestEntity>>(MockBehavior.Strict);
        var mapper = new TestMapper();
        var service = new TestSyncService(repo.Object, mapper);

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        repo.Setup(x => x.UnarchiveAll(It.Is<IReadOnlyList<Guid>>(ids =>
                ids.Count == 2 &&
                ids.Contains(id1) &&
                ids.Contains(id2))))
            .Returns(true);

        service.UnarchiveAll(new[]
        {
            new TestModel { Id = id1, Name = "a" },
            new TestModel { Id = id2, Name = "b" },
            new TestModel { Id = id2, Name = "b-dup" }
        });

        Assert.AreEqual(2, service.PreUnarchiveCalls);
        Assert.AreEqual(2, service.PostUnarchiveCalls);
        repo.Verify(x => x.Unarchive(It.IsAny<Guid>()), Times.Never);
    }
    [TestMethod]
    public void SaveAll_WhenMethodNotAllowed_ThrowsMethodAccess()
    {
        var service = new LockedSyncService(
            Mock.Of<IBaseRepository<TestEntity>>(),
            new TestMapper());

        AssertThrows<MethodAccessException>(() =>
            service.SaveAll(new[] { new TestModel { Id = Guid.Empty, Name = "x" } }));
    }

    [TestMethod]
    public void SaveAll_WhenRepositoryThrows_PassesThroughRepositoryException()
    {
        var repo = new Mock<IBaseRepository<TestEntity>>(MockBehavior.Strict);
        var service = new TestSyncService(repo.Object, new TestMapper());

        repo.Setup(x => x.SaveAll(It.IsAny<IReadOnlyList<TestEntity>>()))
            .Throws(new RepositoryException("TestRepo", "SaveAll", "boom"));

        AssertThrows<RepositoryException>(() =>
            service.SaveAll(new[] { new TestModel { Id = Guid.NewGuid(), Name = "x" } }));
    }

    [TestMethod]
    public void ArchiveAll_WhenEmptyInput_DoesNotCallRepository()
    {
        var repo = new Mock<IBaseRepository<TestEntity>>(MockBehavior.Strict);
        var service = new TestSyncService(repo.Object, new TestMapper());

        service.ArchiveAll(Array.Empty<TestModel>());
        repo.Verify(x => x.ArchiveAll(It.IsAny<IReadOnlyList<Guid>>()), Times.Never);
    }
    private sealed class TestSyncService : BaseService<TestEntity, TestModel>
    {
        public int PreSaveCreateCalls { get; private set; }
        public int PreSaveUpdateCalls { get; private set; }
        public int PostSaveCalls { get; private set; }
        public int PreArchiveCalls { get; private set; }
        public int PostArchiveCalls { get; private set; }
        public int PreUnarchiveCalls { get; private set; }
        public int PostUnarchiveCalls { get; private set; }

        public TestSyncService(
            IBaseRepository<TestEntity> repository,
            IModelMapper<TestEntity, TestModel> mapper,
            IAuditService? audit = null)
            : base(repository, mapper, audit)
        {
            AllowGetById = true;
            AllowSaveAll = true;
            AllowArchive = true;
            AllowUnarchive = true;
        }

        protected override void PreSave(TestModel model, bool isUpdate)
        {
            if (isUpdate) PreSaveUpdateCalls++;
            else PreSaveCreateCalls++;
        }

        protected override void PostSave(TestModel model, bool isUpdate) => PostSaveCalls++;
        protected override void PreArchive(Guid id) => PreArchiveCalls++;
        protected override void PostArchive(Guid id) => PostArchiveCalls++;
        protected override void PreUnarchive(Guid id) => PreUnarchiveCalls++;
        protected override void PostUnarchive(Guid id) => PostUnarchiveCalls++;
    }
    private sealed class LockedSyncService : BaseService<TestEntity, TestModel>
    {
        public LockedSyncService(
            IBaseRepository<TestEntity> repository,
            IModelMapper<TestEntity, TestModel> mapper)
            : base(repository, mapper)
        {
        }
    }
    private static TException AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
            Assert.Fail($"Expected exception {typeof(TException).Name} was not thrown.");
            throw new InvalidOperationException("Unreachable");
        }
        catch (TException ex)
        {
            return ex;
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



