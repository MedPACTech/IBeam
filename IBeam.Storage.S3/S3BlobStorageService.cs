using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using IBeam.Storage.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IBeam.Storage.S3;

public sealed class S3BlobStorageService : IBlobStorageService
{
    private readonly IAmazonS3 _s3;
    private readonly S3BlobStorageOptions _options;
    private readonly ILogger<S3BlobStorageService> _logger;

    public S3BlobStorageService(
        IOptions<S3BlobStorageOptions> options,
        ILogger<S3BlobStorageService> logger)
    {
        _logger = logger;
        _options = options.Value;
        _options.Validate();

        var config = new AmazonS3Config();
        if (!string.IsNullOrWhiteSpace(_options.ServiceUrl))
        {
            config.ServiceURL = _options.ServiceUrl;
            config.ForcePathStyle = _options.ForcePathStyle;
        }
        else
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(_options.Region);
        }

        if (!string.IsNullOrWhiteSpace(_options.AccessKeyId))
        {
            AWSCredentials creds = string.IsNullOrWhiteSpace(_options.SessionToken)
                ? new BasicAWSCredentials(_options.AccessKeyId, _options.SecretAccessKey)
                : new SessionAWSCredentials(_options.AccessKeyId, _options.SecretAccessKey, _options.SessionToken);
            _s3 = new AmazonS3Client(creds, config);
        }
        else
        {
            _s3 = new AmazonS3Client(config);
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
            await EnsureBucketExistsAsync(containerName, ct).ConfigureAwait(false);

            if (!overwrite && await ExistsAsync(containerName, blobName, ct).ConfigureAwait(false))
            {
                throw new BlobStorageException($"Blob already exists: {containerName}/{blobName}");
            }

            if (content.CanSeek)
            {
                content.Position = 0;
            }

            var request = new PutObjectRequest
            {
                BucketName = containerName,
                Key = blobName,
                InputStream = content,
                AutoCloseStream = false,
                ContentType = contentType ?? "application/octet-stream"
            };

            await _s3.PutObjectAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not BlobStorageException)
        {
            _logger.LogError(ex, "S3 save failed. Bucket={Bucket} Key={Key}", containerName, blobName);
            throw new BlobStorageException($"Failed to save blob '{blobName}' in bucket '{containerName}'.", ex);
        }
    }

    public async Task<byte[]?> GetAsync(string containerName, string blobName, CancellationToken ct = default)
    {
        try
        {
            if (!await ExistsAsync(containerName, blobName, ct).ConfigureAwait(false))
            {
                return null;
            }

            var response = await _s3.GetObjectAsync(containerName, blobName, ct).ConfigureAwait(false);
            await using var responseStream = response.ResponseStream;
            using var ms = new MemoryStream();
            await responseStream.CopyToAsync(ms, ct).ConfigureAwait(false);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "S3 get failed. Bucket={Bucket} Key={Key}", containerName, blobName);
            throw new BlobStorageException($"Failed to get blob '{blobName}' in bucket '{containerName}'.", ex);
        }
    }

    public async Task<Stream?> OpenReadAsync(string containerName, string blobName, CancellationToken ct = default)
    {
        try
        {
            if (!await ExistsAsync(containerName, blobName, ct).ConfigureAwait(false))
            {
                return null;
            }

            var response = await _s3.GetObjectAsync(containerName, blobName, ct).ConfigureAwait(false);
            await using var source = response.ResponseStream;
            var ms = new MemoryStream();
            await source.CopyToAsync(ms, ct).ConfigureAwait(false);
            ms.Position = 0;
            return ms;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "S3 open-read failed. Bucket={Bucket} Key={Key}", containerName, blobName);
            throw new BlobStorageException($"Failed to open blob '{blobName}' in bucket '{containerName}'.", ex);
        }
    }

    public async Task<bool> ExistsAsync(string containerName, string blobName, CancellationToken ct = default)
    {
        try
        {
            var request = new GetObjectMetadataRequest
            {
                BucketName = containerName,
                Key = blobName
            };

            await _s3.GetObjectMetadataAsync(request, ct).ConfigureAwait(false);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task DeleteAsync(string containerName, string blobName, bool ignoreIfMissing = true, CancellationToken ct = default)
    {
        try
        {
            if (!ignoreIfMissing && !await ExistsAsync(containerName, blobName, ct).ConfigureAwait(false))
            {
                throw new BlobStorageException($"Blob not found: {containerName}/{blobName}");
            }

            await _s3.DeleteObjectAsync(containerName, blobName, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not BlobStorageException)
        {
            _logger.LogError(ex, "S3 delete failed. Bucket={Bucket} Key={Key}", containerName, blobName);
            throw new BlobStorageException($"Failed to delete blob '{blobName}' in bucket '{containerName}'.", ex);
        }
    }

    public string GetBlobUrl(string containerName, string blobName)
    {
        if (!string.IsNullOrWhiteSpace(_options.ServiceUrl))
        {
            var baseUrl = _options.ServiceUrl.TrimEnd('/');
            if (_options.ForcePathStyle)
            {
                return $"{baseUrl}/{containerName}/{Uri.EscapeDataString(blobName)}";
            }

            return $"{baseUrl}/{Uri.EscapeDataString(blobName)}";
        }

        return $"https://{containerName}.s3.{_options.Region}.amazonaws.com/{Uri.EscapeDataString(blobName)}";
    }

    private async Task EnsureBucketExistsAsync(string bucketName, CancellationToken ct)
    {
        var exists = await AmazonS3Util.DoesS3BucketExistV2Async(_s3, bucketName).ConfigureAwait(false);
        if (!exists)
        {
            await _s3.PutBucketAsync(new PutBucketRequest { BucketName = bucketName }, ct).ConfigureAwait(false);
        }
    }
}
