namespace IBeam.Services.Abstractions;

public interface IAuditTrailSink
{
    Task WriteTransactionAsync(ServiceAuditTransaction transaction, CancellationToken ct = default);

    Task UpsertSelectRollupAsync(ServiceSelectAuditRollup rollup, CancellationToken ct = default);
}

public sealed class NoOpAuditTrailSink : IAuditTrailSink
{
    public Task WriteTransactionAsync(ServiceAuditTransaction transaction, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task UpsertSelectRollupAsync(ServiceSelectAuditRollup rollup, CancellationToken ct = default)
        => Task.CompletedTask;
}

