namespace IBeam.Storage.FileSystem;

public sealed class FileSystemBlobStorageOptions
{
    public const string SectionName = "IBeam:Storage:FileSystem";

    public string RootPath { get; set; } = string.Empty;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RootPath))
        {
            throw new InvalidOperationException("RootPath is required for file-system blob storage.");
        }
    }
}
