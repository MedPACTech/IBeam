namespace IBeam.Repositories.AzureTables;

public sealed class NullEntityLocator : IEntityLocator
{
    public Task<(string PartitionKey, string RowKey)?> FindAsync(
        string scope,
        string entityType,
        string id,
        CancellationToken ct = default)
        => Task.FromResult<(string PartitionKey, string RowKey)?>(null);

    public Task UpsertAsync(
        string scope,
        string entityType,
        string id,
        string partitionKey,
        string rowKey,
        CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DeleteAsync(
        string scope,
        string entityType,
        string id,
        CancellationToken ct = default)
        => Task.CompletedTask;
}
