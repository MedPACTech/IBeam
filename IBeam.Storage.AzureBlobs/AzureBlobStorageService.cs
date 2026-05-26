using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using IBeam.Storage.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IBeam.Storage.AzureBlobs;

public sealed class AzureBlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<AzureBlobStorageService> _logger;

    public AzureBlobStorageService(
        IOptions<AzureBlobStorageOptions> options,
        ILogger<AzureBlobStorageService> logger)
    {
        _logger = logger;
        var settings = options.Value;
        settings.Validate();

        if (!string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            if (settings.UseDevelopmentStorageCompatibility &&
                settings.ConnectionString.Contains("UseDevelopmentStorage=true", StringComparison.OrdinalIgnoreCase))
            {
                var clientOptions = new BlobClientOptions(BlobClientOptions.ServiceVersion.V2021_12_02);
                _blobServiceClient = new BlobServiceClient(settings.ConnectionString, clientOptions);
            }
            else
            {
                _blobServiceClient = new BlobServiceClient(settings.ConnectionString);
            }
        }
        else
        {
            _blobServiceClient = new BlobServiceClient(new Uri(settings.ServiceUri!));
        }
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
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct).ConfigureAwait(false);

            if (content.CanSeek)
            {
                content.Position = 0;
            }

            var blobClient = containerClient.GetBlobClient(blobName);
            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = contentType ?? "application/octet-stream"
                }
            };

            if (!overwrite)
            {
                uploadOptions.Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All };
            }

            await blobClient.UploadAsync(content, uploadOptions, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure blob save failed. Container={Container} Blob={Blob}", containerName, blobName);
            throw new BlobStorageException($"Failed to save blob '{blobName}' in container '{containerName}'.", ex);
        }
    }

    public async Task<byte[]?> GetAsync(string containerName, string blobName, CancellationToken ct = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(blobName);

            var exists = await blobClient.ExistsAsync(ct).ConfigureAwait(false);
            if (!exists.Value)
            {
                return null;
            }

            var download = await blobClient.DownloadStreamingAsync(cancellationToken: ct).ConfigureAwait(false);
            await using var source = download.Value.Content;
            using var ms = new MemoryStream();
            await source.CopyToAsync(ms, ct).ConfigureAwait(false);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure blob get failed. Container={Container} Blob={Blob}", containerName, blobName);
            throw new BlobStorageException($"Failed to get blob '{blobName}' in container '{containerName}'.", ex);
        }
    }

    public async Task<Stream?> OpenReadAsync(string containerName, string blobName, CancellationToken ct = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(blobName);

            var exists = await blobClient.ExistsAsync(ct).ConfigureAwait(false);
            if (!exists.Value)
            {
                return null;
            }

            var download = await blobClient.DownloadStreamingAsync(cancellationToken: ct).ConfigureAwait(false);
            var ms = new MemoryStream();
            await download.Value.Content.CopyToAsync(ms, ct).ConfigureAwait(false);
            ms.Position = 0;
            return ms;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure blob open-read failed. Container={Container} Blob={Blob}", containerName, blobName);
            throw new BlobStorageException($"Failed to open blob '{blobName}' in container '{containerName}'.", ex);
        }
    }

    public async Task<bool> ExistsAsync(string containerName, string blobName, CancellationToken ct = default)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);
        var exists = await blobClient.ExistsAsync(ct).ConfigureAwait(false);
        return exists.Value;
    }

    public async Task DeleteAsync(string containerName, string blobName, bool ignoreIfMissing = true, CancellationToken ct = default)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(blobName);

            if (ignoreIfMissing)
            {
                await blobClient.DeleteIfExistsAsync(cancellationToken: ct).ConfigureAwait(false);
            }
            else
            {
                await blobClient.DeleteAsync(cancellationToken: ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure blob delete failed. Container={Container} Blob={Blob}", containerName, blobName);
            throw new BlobStorageException($"Failed to delete blob '{blobName}' in container '{containerName}'.", ex);
        }
    }

    public string GetBlobUrl(string containerName, string blobName)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        return containerClient.GetBlobClient(blobName).Uri.ToString();
    }
}
