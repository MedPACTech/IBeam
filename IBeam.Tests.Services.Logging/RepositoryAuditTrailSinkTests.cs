using IBeam.Repositories.Abstractions;
using IBeam.Services.Abstractions;
using IBeam.Services.Logging;
using Moq;

namespace IBeam.Tests.Services.Logging;

[TestClass]
public sealed class RepositoryAuditTrailSinkTests
{
    [TestMethod]
    public async Task UpsertSelectRollupAsync_WhenMissing_CreatesEntry()
    {
        var repo = new Mock<IBaseRepositoryAsync<ServiceAuditLogEntry>>(MockBehavior.Strict);
        var sink = new RepositoryAuditTrailSink(repo.Object);

        var rollup = new ServiceSelectAuditRollup
        {
            DateUtc = new DateOnly(2026, 3, 23),
            ServiceName = "PatientService",
            EntityName = "Patient",
            Operation = ServiceAuditOperation.GetAll,
            QuerySignature = "GetAll:abc",
            Count = 1
        };

        var id = RepositoryAuditTrailSink.BuildDailyRollupId(rollup);

        repo.Setup(x => x.GetByIdAsync(id, true, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ServiceAuditLogEntry?)null);
        repo.Setup(x => x.SaveAsync(It.Is<ServiceAuditLogEntry>(e => e.Id == id && e.Count == 1 && e.IsSelectRollup), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ServiceAuditLogEntry e, CancellationToken _) => e);

        await sink.UpsertSelectRollupAsync(rollup);

        repo.Verify(x => x.SaveAsync(It.IsAny<ServiceAuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task UpsertSelectRollupAsync_WhenExisting_IncrementsCount()
    {
        var repo = new Mock<IBaseRepositoryAsync<ServiceAuditLogEntry>>(MockBehavior.Strict);
        var sink = new RepositoryAuditTrailSink(repo.Object);

        var rollup = new ServiceSelectAuditRollup
        {
            DateUtc = new DateOnly(2026, 3, 23),
            ServiceName = "PatientService",
            EntityName = "Patient",
            Operation = ServiceAuditOperation.GetAll,
            QuerySignature = "GetAll:abc",
            Count = 1
        };

        var id = RepositoryAuditTrailSink.BuildDailyRollupId(rollup);
        var existing = new ServiceAuditLogEntry
        {
            Id = id,
            IsSelectRollup = true,
            Count = 5
        };

        repo.Setup(x => x.GetByIdAsync(id, true, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        repo.Setup(x => x.SaveAsync(It.Is<ServiceAuditLogEntry>(e => e.Id == id && e.Count == 6), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ServiceAuditLogEntry e, CancellationToken _) => e);

        await sink.UpsertSelectRollupAsync(rollup);

        repo.Verify(x => x.SaveAsync(It.IsAny<ServiceAuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task WriteTransactionAsync_PersistsEntry()
    {
        var repo = new Mock<IBaseRepositoryAsync<ServiceAuditLogEntry>>(MockBehavior.Strict);
        var sink = new RepositoryAuditTrailSink(repo.Object);

        var txn = new ServiceAuditTransaction
        {
            ServiceName = "PatientService",
            EntityName = "Patient",
            Operation = ServiceAuditOperation.Update,
            Action = "patients.update",
            EntityId = Guid.NewGuid(),
            OriginalJson = "{\"a\":1}",
            TransformedJson = "{\"a\":2}",
            IpAddress = "127.0.0.1"
        };

        repo.Setup(x => x.SaveAsync(It.Is<ServiceAuditLogEntry>(e =>
                !e.IsSelectRollup &&
                e.ServiceName == "PatientService" &&
                e.Operation == "Update" &&
                e.Action == "patients.update" &&
                e.BeforeJson == "{\"a\":1}" &&
                e.AfterJson == "{\"a\":2}" &&
                e.IpAddress == "127.0.0.1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ServiceAuditLogEntry e, CancellationToken _) => e);

        await sink.WriteTransactionAsync(txn);

        repo.Verify(x => x.SaveAsync(It.IsAny<ServiceAuditLogEntry>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
