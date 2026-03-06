using IBeam.Repositories.Abstractions;

namespace IBeam.Repositories.AzureTables;

public interface IAzureTablesBatchStore<T> where T : class, IEntity
{
    Task SubmitBatchAsync(
        string partitionKey,
        IReadOnlyList<BatchAction<T>> actions,
        CancellationToken ct = default);

    Task SubmitBatchesAsync(
        string partitionKey,
        IReadOnlyList<BatchAction<T>> actions,
        int chunkSize = 100,
        CancellationToken ct = default);
}

