namespace IBeam.Storage.AzureBlobs;

public sealed class AzureBlobStorageOptions
{
    public const string SectionName = "IBeam:Storage:AzureBlobs";

    public string? ConnectionString { get; set; }

    public string? ServiceUri { get; set; }

    public bool UseDevelopmentStorageCompatibility { get; set; } = true;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString) && string.IsNullOrWhiteSpace(ServiceUri))
        {
            throw new InvalidOperationException("Either ConnectionString or ServiceUri must be configured for Azure Blob storage.");
        }

        if (!string.IsNullOrWhiteSpace(ServiceUri) && !Uri.TryCreate(ServiceUri, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("ServiceUri must be a valid absolute URI.");
        }
    }
}
