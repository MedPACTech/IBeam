# IBeam.Storage.Abstractions

`IBeam.Storage.Abstractions` contains provider-agnostic blob/object storage contracts for IBeam applications.

```powershell
dotnet add package IBeam.Storage.Abstractions
```

## When To Use This

- You need services to save and read files without knowing the storage provider.
- You want the same service code to work with Azure Blob Storage, S3, local disk, or a custom provider.
- You are building a storage provider package.

## What This Package Contains

| Type | Purpose |
|---|---|
| `IBlobStorageService` | Common interface for saving, reading, existence checks, deletion, and URL generation. |
| `BlobStorageException` | Consistent storage exception type for provider failures. |

## Architecture Fit

Storage is an infrastructure dependency used by services. Services should decide when files are written or deleted, then call `IBlobStorageService`. APIs should not put file business rules directly in controllers.

## Code Example

```csharp
public sealed class ProductImageService
{
    private readonly IBlobStorageService _storage;

    public ProductImageService(IBlobStorageService storage)
    {
        _storage = storage;
    }

    public async Task SaveImageAsync(Guid tenantId, Guid productId, Stream image, CancellationToken ct)
    {
        await _storage.SaveAsync(
            containerName: $"tenant-{tenantId:N}",
            blobName: $"products/{productId:N}/image.jpg",
            content: image,
            contentType: "image/jpeg",
            overwrite: true,
            ct: ct);
    }
}
```

## Methods

| Method | Purpose |
|---|---|
| `SaveAsync` | Writes a blob/object, optionally preventing overwrite. |
| `GetAsync` | Reads a blob/object into a byte array. |
| `OpenReadAsync` | Opens a readable stream without forcing byte-array materialization. |
| `ExistsAsync` | Checks whether a blob/object exists. |
| `DeleteAsync` | Deletes a blob/object, optionally ignoring missing files. |
| `GetBlobUrl` | Returns the provider URL/path for a blob/object. |

## Data Storage

This package does not create containers, buckets, directories, or tables. Provider packages own physical storage behavior.

| Provider Package | Backing Store |
|---|---|
| `IBeam.Storage.AzureBlobs` | Azure Blob Storage |
| `IBeam.Storage.S3` | AWS S3 or S3-compatible storage |
| `IBeam.Storage.FileSystem` | Local or mounted file system |

## Service Operations, Auditing, And Permissions

File writes/deletes should normally be initiated by service methods that are tagged with `[IBeamOperation]`. The storage provider should stay focused on object IO and should not own business authorization rules.

## Extended Docs And Agent Guidance

- AI prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Root implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- Service logging and audit: [`../docs/service-logging-and-audit.md`](../docs/service-logging-and-audit.md)
- Service operation permissions: [`../docs/service-operation-permissions.md`](../docs/service-operation-permissions.md)

## Version Notes

- Targets `net10.0`.
- Package version is assigned by the repository release workflow.
