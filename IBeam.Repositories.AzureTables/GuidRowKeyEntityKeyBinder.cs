using IBeam.Repositories.Abstractions;

namespace IBeam.Repositories.AzureTables;

public sealed class GuidRowKeyEntityKeyBinder<T> : IEntityKeyBinder<T>
    where T : class, IEntity
{
    private static readonly System.Reflection.PropertyInfo? PartitionKeyProperty =
        typeof(T).GetProperty("PartitionKey", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);

    private static readonly System.Reflection.PropertyInfo? RowKeyProperty =
        typeof(T).GetProperty("RowKey", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);

    public void BindFromKeys(T entity, string partitionKey, string rowKey)
    {
        ArgumentNullException.ThrowIfNull(entity);

        // Safe default: only hydrate Id when empty and RowKey is a Guid.
        if (entity.Id == Guid.Empty && Guid.TryParse(rowKey, out var id))
            entity.Id = id;

        if (PartitionKeyProperty?.CanWrite == true && PartitionKeyProperty.PropertyType == typeof(string))
            PartitionKeyProperty.SetValue(entity, partitionKey);

        if (RowKeyProperty?.CanWrite == true && RowKeyProperty.PropertyType == typeof(string))
            RowKeyProperty.SetValue(entity, rowKey);
    }
}

