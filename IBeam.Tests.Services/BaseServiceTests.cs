using IBeam.Repositories.Abstractions;
using IBeam.Repositories.Core;
using IBeam.Services.Abstractions;
using IBeam.Services.Core;
using Microsoft.Extensions.Options;
using Moq;

namespace IBeam.Tests.Services;

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
        repo.Setup(x => x.GetById(updateId, true, true))
            .Returns(new TestEntity { Id = updateId, Name = "existing" });

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
        var id = Guid.NewGuid();

        repo.Setup(x => x.GetById(id, true, true)).Returns((TestEntity?)null);
        repo.Setup(x => x.SaveAll(It.IsAny<IReadOnlyList<TestEntity>>()))
            .Throws(new RepositoryException("TestRepo", "SaveAll", "boom"));

        AssertThrows<RepositoryException>(() =>
            service.SaveAll(new[] { new TestModel { Id = id, Name = "x" } }));
    }

    [TestMethod]
    public void ArchiveAll_WhenEmptyInput_DoesNotCallRepository()
    {
        var repo = new Mock<IBaseRepository<TestEntity>>(MockBehavior.Strict);
        var service = new TestSyncService(repo.Object, new TestMapper());

        service.ArchiveAll(Array.Empty<TestModel>());
        repo.Verify(x => x.ArchiveAll(It.IsAny<IReadOnlyList<Guid>>()), Times.Never);
    }

    [TestMethod]
    public void Delete_LogsTypedDeleteAudit_WhenEntityExists()
    {
        var id = Guid.NewGuid();
        var entity = new TestEntity { Id = id, Name = "to-delete" };

        var repo = new Mock<IBaseRepository<TestEntity>>(MockBehavior.Strict);
        var mapper = new TestMapper();
        var audit = new Mock<IEntityAuditService<TestEntity>>(MockBehavior.Strict);
        var service = new DeleteEnabledSyncService(repo.Object, mapper, audit.Object);

        repo.Setup(x => x.GetById(id, true, true)).Returns(entity);
        repo.Setup(x => x.Delete(id));
        audit.Setup(x => x.LogDelete(It.Is<TestEntity>(e => e.Id == id)));

        service.Delete(id);

        audit.Verify(x => x.LogDelete(It.IsAny<TestEntity>()), Times.Once);
    }

    [TestMethod]
    public void Save_WhenServiceAuditEnabled_WritesConventionAction()
    {
        var repo = new Mock<IBaseRepository<TestEntity>>(MockBehavior.Strict);
        var mapper = new TestMapper();
        var sink = new Mock<IAuditTrailSink>(MockBehavior.Strict);
        var service = new TestSyncService(
            repo.Object,
            mapper,
            auditTrailSink: sink.Object,
            auditOptionsMonitor: OptionsMonitor(new ServiceAuditOptions { Enabled = true }));

        repo.Setup(x => x.Save(It.Is<TestEntity>(e => e.Name == "created")))
            .Returns((TestEntity e) => e with { Id = Guid.NewGuid() });

        sink.Setup(x => x.WriteTransactionAsync(It.Is<ServiceAuditTransaction>(t =>
                t.Operation == ServiceAuditOperation.Create &&
                t.Action == "test.create" &&
                t.EntityName == "test" &&
                t.OriginalJson == null &&
                t.TransformedJson != null), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var saved = service.Save(new TestModel { Name = "created" });

        Assert.AreNotEqual(Guid.Empty, saved.Id);
        sink.Verify(x => x.WriteTransactionAsync(It.IsAny<ServiceAuditTransaction>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public void Save_WhenServiceAuditDisabledByServiceOption_DoesNotWriteAudit()
    {
        var repo = new Mock<IBaseRepository<TestEntity>>(MockBehavior.Strict);
        var mapper = new TestMapper();
        var sink = new Mock<IAuditTrailSink>(MockBehavior.Strict);
        var options = new ServiceAuditOptions { Enabled = true };
        options.Services[nameof(TestSyncService)] = new ServiceAuditServiceOptions { Enabled = false };
        var service = new TestSyncService(
            repo.Object,
            mapper,
            auditTrailSink: sink.Object,
            auditOptionsMonitor: OptionsMonitor(options));

        repo.Setup(x => x.Save(It.Is<TestEntity>(e => e.Name == "created")))
            .Returns((TestEntity e) => e with { Id = Guid.NewGuid() });

        service.Save(new TestModel { Name = "created" });

        sink.Verify(x => x.WriteTransactionAsync(It.IsAny<ServiceAuditTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Archive_WhenServiceAuditEnabled_WritesBeforeAndAfter()
    {
        var id = Guid.NewGuid();
        var repo = new Mock<IBaseRepository<TestEntity>>(MockBehavior.Strict);
        var mapper = new TestMapper();
        var sink = new Mock<IAuditTrailSink>(MockBehavior.Strict);
        var service = new TestSyncService(
            repo.Object,
            mapper,
            auditTrailSink: sink.Object,
            auditOptionsMonitor: OptionsMonitor(new ServiceAuditOptions { Enabled = true }));

        repo.SetupSequence(x => x.GetById(id, true, true))
            .Returns(new TestEntity { Id = id, Name = "active" })
            .Returns(new TestEntity { Id = id, Name = "archived" });
        repo.Setup(x => x.Archive(id)).Returns(true);

        sink.Setup(x => x.WriteTransactionAsync(It.Is<ServiceAuditTransaction>(t =>
                t.Operation == ServiceAuditOperation.Archive &&
                t.Action == "test.archive" &&
                t.OriginalJson != null &&
                t.TransformedJson != null), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        service.Archive(id);

        sink.Verify(x => x.WriteTransactionAsync(It.IsAny<ServiceAuditTransaction>(), It.IsAny<CancellationToken>()), Times.Once);
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
            IAuditService? audit = null,
            IAuditTrailSink? auditTrailSink = null,
            IOptionsMonitor<ServiceAuditOptions>? auditOptionsMonitor = null)
            : base(repository, mapper, audit, auditTrailSink: auditTrailSink, auditOptionsMonitor: auditOptionsMonitor)
        {
            AllowGetById = true;
            AllowSave = true;
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

    private sealed class DeleteEnabledSyncService : BaseService<TestEntity, TestModel>
    {
        public DeleteEnabledSyncService(
            IBaseRepository<TestEntity> repository,
            IModelMapper<TestEntity, TestModel> mapper,
            IAuditService audit)
            : base(repository, mapper, audit)
        {
            AllowDelete = true;
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

    private static IOptionsMonitor<ServiceAuditOptions> OptionsMonitor(ServiceAuditOptions options)
    {
        var monitor = new Mock<IOptionsMonitor<ServiceAuditOptions>>();
        monitor.SetupGet(x => x.CurrentValue).Returns(options);
        return monitor.Object;
    }
}




