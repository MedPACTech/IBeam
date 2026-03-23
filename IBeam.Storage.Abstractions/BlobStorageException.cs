namespace IBeam.Storage.Abstractions;

public sealed class BlobStorageException : Exception
{
    public BlobStorageException(string message)
        : base(message)
    {
    }

    public BlobStorageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
