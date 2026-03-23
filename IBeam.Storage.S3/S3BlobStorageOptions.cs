namespace IBeam.Storage.S3;

public sealed class S3BlobStorageOptions
{
    public const string SectionName = "IBeam:Storage:S3";

    public string? Region { get; set; }

    public string? AccessKeyId { get; set; }

    public string? SecretAccessKey { get; set; }

    public string? SessionToken { get; set; }

    public string? ServiceUrl { get; set; }

    public bool ForcePathStyle { get; set; } = true;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Region) && string.IsNullOrWhiteSpace(ServiceUrl))
        {
            throw new InvalidOperationException("Either Region or ServiceUrl must be set for S3 storage.");
        }

        var hasAccess = !string.IsNullOrWhiteSpace(AccessKeyId);
        var hasSecret = !string.IsNullOrWhiteSpace(SecretAccessKey);
        if (hasAccess != hasSecret)
        {
            throw new InvalidOperationException("AccessKeyId and SecretAccessKey must both be provided together.");
        }
    }
}
