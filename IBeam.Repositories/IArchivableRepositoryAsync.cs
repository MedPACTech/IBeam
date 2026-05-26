namespace IBeam.Repositories.Abstractions;

public interface IArchivableRepositoryAsync<T> where T : class, IEntity
{
    Task<bool> ArchiveAsync(Guid id, CancellationToken ct = default);
    Task<bool> UnarchiveAsync(Guid id, CancellationToken ct = default);
    Task<bool> ArchiveAllAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);
    Task<bool> UnarchiveAllAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);
}

