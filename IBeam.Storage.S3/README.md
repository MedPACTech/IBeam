# IBeam.Storage.S3

`IBeam.Storage.S3` implements `IBlobStorageService` with AWS S3 or S3-compatible object storage.

```powershell
dotnet add package IBeam.Storage.S3
```

## When To Use This

- You want IBeam file/blob operations backed by AWS S3.
- You use S3-compatible storage through a custom service URL.
- You want services to depend on `IBlobStorageService` instead of AWS SDK types.

## What This Package Contains

| Area | Type(s) | Purpose |
|---|---|---|
| Provider | `S3BlobStorageService` | Implements save/read/delete/existence/URL operations. |
| Options | `S3BlobStorageOptions` | Configures region, credentials, service URL, and path-style behavior. |
| DI | `AddIBeamS3BlobStorage(IConfiguration)` | Registers S3 as `IBlobStorageService`. |

## Quick Start

```csharp
using IBeam.Storage.S3;

builder.Services.AddIBeamS3BlobStorage(builder.Configuration);
```

Configuration with AWS region:

```json
{
  "IBeam": {
    "Storage": {
      "S3": {
        "Region": "us-east-1",
        "AccessKeyId": "<optional>",
        "SecretAccessKey": "<optional>",
        "ForcePathStyle": true
      }
    }
  }
}
```

Configuration with an S3-compatible endpoint:

```json
{
  "IBeam": {
    "Storage": {
      "S3": {
        "ServiceUrl": "http://localhost:9000",
        "AccessKeyId": "minio",
        "SecretAccessKey": "minio123",
        "ForcePathStyle": true
      }
    }
  }
}
```

## Configuration

| Setting | Default | Purpose |
|---|---:|---|
| `Region` | empty | AWS region. Required unless `ServiceUrl` is set. |
| `AccessKeyId` | empty | Optional explicit access key. Must be paired with `SecretAccessKey`. |
| `SecretAccessKey` | empty | Optional explicit secret key. Must be paired with `AccessKeyId`. |
| `SessionToken` | empty | Optional temporary credential token. |
| `ServiceUrl` | empty | Custom S3-compatible endpoint. |
| `ForcePathStyle` | `true` | Uses path-style URLs, commonly needed for local/S3-compatible providers. |

Either `Region` or `ServiceUrl` is required.

## Storage Shape

This package creates or uses S3 buckets by name. It does not create Azure Tables.

| Concept | IBeam Name | S3 Name |
|---|---|---|
| Storage group | `containerName` | Bucket |
| Object path | `blobName` | Object key |
| Content type | `contentType` | Object content type |

## Code Example

```csharp
await storage.SaveAsync(
    "tenant-225925cc995e4584a63b4f2cb4f38f6f",
    "exports/month-end.csv",
    csvStream,
    contentType: "text/csv",
    overwrite: false,
    ct);
```

## Extended Docs And Agent Guidance

- AI prompt: [`.agent/prompt.md`](./.agent/prompt.md)
- Root implementation guide: [`../.agent/implementation-guide.md`](../.agent/implementation-guide.md)
- Storage abstractions: [`../IBeam.Storage.Abstractions/README.md`](../IBeam.Storage.Abstractions/README.md)

Agents should keep object naming conventions in services and use this provider only for S3 IO.

## Troubleshooting

| Problem | Likely Cause | Fix |
|---|---|---|
| Startup fails | Neither `Region` nor `ServiceUrl` is configured | Configure one under `IBeam:Storage:S3`. |
| Credential validation fails | Only one of key/secret was set | Provide both or rely on default AWS credential resolution. |
| Local S3-compatible storage fails | Path-style addressing mismatch | Keep `ForcePathStyle` enabled for MinIO/local endpoints. |

## Version Notes

- Targets `net10.0`.
- Uses AWS S3 SDK.
- Package version is assigned by the repository release workflow.
