using IBeam.Repositories.Abstractions;

namespace IBeam.Repositories.AzureTables;

public sealed class AzureEntityKeyResolver<T> : IAzureEntityKeyResolver<T>
    where T : class, IEntity
{
    private readonly IAzureTablePartitionKeyStrategy<T> _partitionKeyStrategy;
    private readonly AzureEntityMappingOptions<T>? _mapping;
    private readonly IAzureEntityKeyFormatter _keyFormatter;

    public AzureEntityKeyResolver(
        IAzureTablePartitionKeyStrategy<T>? partitionKeyStrategy = null,
        AzureEntityMappingOptions<T>? mapping = null,
        IAzureEntityKeyFormatter? keyFormatter = null)
    {
        _partitionKeyStrategy = partitionKeyStrategy ?? AzureTablePartitionKeyStrategies.Default<T>();
        _mapping = mapping;
        _keyFormatter = keyFormatter ?? new AzureEntityKeyFormatter("N", false);
    }

    public AzureEntityKey ResolveWriteKey(Guid? tenantId, T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (_mapping?.WriteKey is not null)
            return _mapping.WriteKey(tenantId, entity);

        return new AzureEntityKey
        {
            PartitionKey = _partitionKeyStrategy.GetPartitionKeyForWrite(tenantId, entity),
            RowKey = _keyFormatter.Format(entity.Id)
        };
    }
}
