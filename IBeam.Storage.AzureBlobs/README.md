# IBeam.Storage.AzureBlobs

`IBeam.Storage.AzureBlobs` implements `IBlobStorageService` with Azure Blob Storage.

```powershell
dotnet add package IBeam.Storage.AzureBlobs
```

## When To Use This

- You want IBeam file/blob operations backed by Azure Blob Storage.
- You are deploying to Azure and want to use storage account containers.
- You want services to depend on `IBlobStorageService` instead of Azure SDK types.

## What This Package Contains

| Area | Type(s) | Purpose |
|---|---|---|
| Provider | `AzureBlobStorageService` | Implements blob save/read/delete/existence/URL operations. |
| Options | `AzureBlobStorageOptions` | Configures connection string or service URI. |
| DI | `AddIBeamAzureBlobStorage(IConfiguration)` | Registers Azure Blob Storage as `IBlobStorageService`. |

## Quick Start

```csharp
using IBeam.Storage.AzureBlobs;

builder.Services.AddIBeamAzureBlobStorage(builder.Configuration);
```

Configuration with a connection string:

```json
{
  "IBeam": {
    "Storage": {
      "AzureBlobs": {
        "ConnectionString": "<storage-connection-string>",
        "UseDevelopmentStorageCompatibility": true
      }
    }
  }
}
```

Configuration with a service URI:

```json
{
  "IBeam": {
    "Storage": {
      "AzureBlobs": {
        "ServiceUri": "https://account.blob.core.windows.net"
      }
    }
  }
}
```

## Configuration

| Setting | Default | Purpose |
|---|---:|---|
| `ConnectionString` | empty | Storage account or development storage connection string. |
| `ServiceUri` | empty | Absolute Blob service URI. |
| `UseDevelopmentStorageCompatibility` | `true` | Applies compatibility behavior for local Azurite/development storage when using a connection string. |

Either `ConnectionString` or `ServiceUri` is required.

## Storage Shape

This package creates or uses Azure Blob containers by name. It does not create Azure Tables.

| Concept | IBeam Name | Azure Blob Name |
|---|---|---|
| Storage group | `containerName` | Blob container |
| Object path | `blobName` | Blob name/key |
| Content type | `contentType` | Blob HTTP content type |

## Code Example

```csharp
await storage.SaveAsync(
    "tenant-225925cc995e4584a63b4f2cb4f38f6f",
    "products/cc0b3/image.jpg",
    imageStream,
    contentType: "image/jpeg",
    overwrite: true,
    ct);
```

## Extended Docs And Agent Guidance

- AI prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Root implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- Storage abstractions: [`../IBeam.Storage.Abstractions/README.md`](../IBeam.Storage.Abstractions/README.md)

Agents should keep file naming rules in services and use this provider only for Azure Blob IO.

## Troubleshooting

| Problem | Likely Cause | Fix |
|---|---|---|
| Startup fails | Neither `ConnectionString` nor `ServiceUri` is configured | Configure one of them under `IBeam:Storage:AzureBlobs`. |
| Save fails with container error | Invalid container name or insufficient permission | Use Azure-valid lowercase container names and verify credentials. |
| URL is not publicly accessible | Container/account access policy blocks it | Use signed URLs or a host-controlled download endpoint when needed. |

## Version Notes

- Targets `net10.0`.
- Uses Azure Storage Blob SDK.
- Package version is assigned by the repository release workflow.
