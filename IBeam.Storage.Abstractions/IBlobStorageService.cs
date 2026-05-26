namespace IBeam.Storage.Abstractions;

public interface IBlobStorageService
{
    Task SaveAsync(
        string containerName,
        string blobName,
        Stream content,
        string? contentType = null,
        bool overwrite = true,
        CancellationToken ct = default);

    Task<byte[]?> GetAsync(string containerName, string blobName, CancellationToken ct = default);

    Task<Stream?> OpenReadAsync(string containerName, string blobName, CancellationToken ct = default);

    Task<bool> ExistsAsync(string containerName, string blobName, CancellationToken ct = default);

    Task DeleteAsync(string containerName, string blobName, bool ignoreIfMissing = true, CancellationToken ct = default);

    string GetBlobUrl(string containerName, string blobName);
}
