using IBeam.Repositories.Abstractions;

namespace IBeam.Repositories.AzureTables;

public interface IEntityKeyBinder<T>
    where T : class, IEntity
{
    void BindFromKeys(T entity, string partitionKey, string rowKey);
}

