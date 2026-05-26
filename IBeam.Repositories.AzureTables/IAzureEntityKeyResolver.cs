using IBeam.Repositories.Abstractions;

namespace IBeam.Repositories.AzureTables;

public interface IAzureEntityKeyResolver<T>
    where T : class, IEntity
{
    AzureEntityKey ResolveWriteKey(Guid? tenantId, T entity);
}
