using IBeam.Repositories.Abstractions;

namespace IBeam.Repositories.AzureTables;

public sealed class GuidRowKeyEntityKeyBinder<T> : IEntityKeyBinder<T>
    where T : class, IEntity
{
    public void BindFromKeys(T entity, string partitionKey, string rowKey)
    {
        ArgumentNullException.ThrowIfNull(entity);

        // Safe default: only hydrate when Id is empty and RowKey is a Guid.
        if (entity.Id != Guid.Empty)
            return;

        if (Guid.TryParse(rowKey, out var id))
            entity.Id = id;
    }
}

