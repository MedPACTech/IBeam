namespace IBeam.Repositories.AzureTables;

public interface IEntityLocator
{
    Task<(string PartitionKey, string RowKey)?> FindAsync(
        string scope,
        string entityType,
        string id,
        CancellationToken ct = default);

    Task UpsertAsync(
        string scope,
        string entityType,
        string id,
        string partitionKey,
        string rowKey,
        CancellationToken ct = default);

    Task DeleteAsync(
        string scope,
        string entityType,
        string id,
        CancellationToken ct = default);
}

