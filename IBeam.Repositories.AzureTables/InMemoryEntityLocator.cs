using System.Collections.Concurrent;

namespace IBeam.Repositories.AzureTables;

public sealed class InMemoryEntityLocator : IEntityLocator
{
    private readonly ConcurrentDictionary<string, (string PartitionKey, string RowKey)> _map =
        new(StringComparer.OrdinalIgnoreCase);

    public Task<(string PartitionKey, string RowKey)?> FindAsync(
        string scope,
        string entityType,
        string id,
        CancellationToken ct = default)
    {
        var key = BuildKey(scope, entityType, id);
        return Task.FromResult(_map.TryGetValue(key, out var value)
            ? ((string PartitionKey, string RowKey)?)value
            : null);
    }

    public Task UpsertAsync(
        string scope,
        string entityType,
        string id,
        string partitionKey,
        string rowKey,
        CancellationToken ct = default)
    {
        var key = BuildKey(scope, entityType, id);
        _map[key] = (partitionKey, rowKey);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        string scope,
        string entityType,
        string id,
        CancellationToken ct = default)
    {
        var key = BuildKey(scope, entityType, id);
        _map.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    private static string BuildKey(string scope, string entityType, string id)
        => $"{(scope ?? string.Empty).Trim()}|{(entityType ?? string.Empty).Trim()}|{(id ?? string.Empty).Trim()}";
}

