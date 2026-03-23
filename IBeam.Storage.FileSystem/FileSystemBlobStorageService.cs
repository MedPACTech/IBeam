using IBeam.Storage.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IBeam.Storage.FileSystem;

public sealed class FileSystemBlobStorageService : IBlobStorageService
{
    private readonly string _rootPath;
    private readonly ILogger<FileSystemBlobStorageService> _logger;

    public FileSystemBlobStorageService(
        IOptions<FileSystemBlobStorageOptions> options,
        ILogger<FileSystemBlobStorageService> logger)
    {
        _logger = logger;

        var settings = options.Value;
        settings.Validate();

        _rootPath = Path.GetFullPath(settings.RootPath);
        Directory.CreateDirectory(_rootPath);
    }

    public async Task SaveAsync(
        string containerName,
        string blobName,
        Stream content,
        string? contentType = null,
        bool overwrite = true,
        CancellationToken ct = default)
    {
        try
        {
            var path = ResolvePath(containerName, blobName);
            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);

            if (!overwrite && File.Exists(path))
            {
                throw new BlobStorageException($"Blob already exists: {containerName}/{blobName}");
            }

            if (content.CanSeek)
            {
                content.Position = 0;
            }

            await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await content.CopyToAsync(fs, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not BlobStorageException)
        {
            _logger.LogError(ex, "File-system blob save failed. Container={Container} Blob={Blob}", containerName, blobName);
            throw new BlobStorageException($"Failed to save blob '{blobName}' in container '{containerName}'.", ex);
        }
    }

    public async Task<byte[]?> GetAsync(string containerName, string blobName, CancellationToken ct = default)
    {
        var path = ResolvePath(containerName, blobName);
        if (!File.Exists(path))
        {
            return null;
        }

        return await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
    }

    public Task<Stream?> OpenReadAsync(string containerName, string blobName, CancellationToken ct = default)
    {
        var path = ResolvePath(containerName, blobName);
        if (!File.Exists(path))
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult<Stream?>(stream);
    }

    public Task<bool> ExistsAsync(string containerName, string blobName, CancellationToken ct = default)
    {
        var path = ResolvePath(containerName, blobName);
        return Task.FromResult(File.Exists(path));
    }

    public Task DeleteAsync(string containerName, string blobName, bool ignoreIfMissing = true, CancellationToken ct = default)
    {
        var path = ResolvePath(containerName, blobName);
        if (!File.Exists(path))
        {
            if (ignoreIfMissing)
            {
                return Task.CompletedTask;
            }

            throw new BlobStorageException($"Blob not found: {containerName}/{blobName}");
        }

        File.Delete(path);
        return Task.CompletedTask;
    }

    public string GetBlobUrl(string containerName, string blobName)
    {
        var path = ResolvePath(containerName, blobName);
        return path;
    }

    private string ResolvePath(string containerName, string blobName)
    {
        var safeContainer = containerName.Trim();
        if (string.IsNullOrWhiteSpace(safeContainer))
        {
            throw new BlobStorageException("Container name is required.");
        }

        var normalizedBlob = blobName
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .Trim();

        if (string.IsNullOrWhiteSpace(normalizedBlob))
        {
            throw new BlobStorageException("Blob name is required.");
        }

        if (ContainsTraversal(safeContainer) || ContainsTraversal(normalizedBlob))
        {
            throw new BlobStorageException("Invalid blob path.");
        }

        var combined = Path.Combine(_rootPath, safeContainer, normalizedBlob);
        var fullPath = Path.GetFullPath(combined);

        if (!fullPath.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new BlobStorageException("Invalid blob path.");
        }

        return fullPath;
    }

    private static bool ContainsTraversal(string value)
    {
        var parts = value.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Any(part => part == "..");
    }
}
