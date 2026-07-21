# IBeam.Storage.FileSystem

`IBeam.Storage.FileSystem` implements `IBlobStorageService` with local or mounted file-system storage.

```powershell
dotnet add package IBeam.Storage.FileSystem
```

## When To Use This

- You need local development or test storage.
- You deploy with a mounted disk/share and do not need cloud blob APIs.
- You want a simple provider behind the same `IBlobStorageService` abstraction.

## What This Package Contains

| Area | Type(s) | Purpose |
|---|---|---|
| Provider | `FileSystemBlobStorageService` | Saves, reads, streams, deletes, and resolves file URLs/paths. |
| Options | `FileSystemBlobStorageOptions` | Configures the root directory. |
| DI | `AddIBeamFileSystemBlobStorage(IConfiguration)` | Registers the file-system provider as `IBlobStorageService`. |

## Quick Start

```csharp
using IBeam.Storage.FileSystem;

builder.Services.AddIBeamFileSystemBlobStorage(builder.Configuration);
```

Configuration:

```json
{
  "IBeam": {
    "Storage": {
      "FileSystem": {
        "RootPath": "C:\\IBeam\\Blobs"
      }
    }
  }
}
```

## Configuration

| Setting | Default | Purpose |
|---|---:|---|
| `RootPath` | required | Base directory where containers and blobs are stored. |

## Storage Shape

This package does not create tables. It stores files under the configured root path.

| IBeam Name | File-System Meaning |
|---|---|
| `containerName` | First directory under `RootPath`. |
| `blobName` | Relative file path inside the container directory. |

Example:

```text
RootPath:      C:\IBeam\Blobs
containerName: tenant-abc
blobName:      products/123/image.jpg
full path:     C:\IBeam\Blobs\tenant-abc\products\123\image.jpg
```

The provider validates paths to prevent traversal outside `RootPath`.

## Extended Docs And Agent Guidance

- AI prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Root implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- Storage abstractions: [`../IBeam.Storage.Abstractions/README.md`](../IBeam.Storage.Abstractions/README.md)

Agents should use this provider for local/dev scenarios and avoid hard-coding local paths inside domain services.

## Troubleshooting

| Problem | Likely Cause | Fix |
|---|---|---|
| Startup fails | `RootPath` is missing | Configure `IBeam:Storage:FileSystem:RootPath`. |
| Save fails | Directory permissions are missing | Grant the app write access to `RootPath`. |
| Blob path rejected | Path traversal or empty path detected | Use clean relative blob names. |

## Version Notes

- Targets `net10.0`.
- Package version is assigned by the repository release workflow.
