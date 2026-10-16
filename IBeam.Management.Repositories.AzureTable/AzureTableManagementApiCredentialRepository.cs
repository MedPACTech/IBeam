using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using IBeam.Management.Entities;

namespace IBeam.Management.Repositories.AzureTable;

/// <summary>One entity, point reads and conditional writes. Never creates tables or performs business authorization.</summary>
public sealed class AzureTableManagementApiCredentialRepository(TableClient table) : IManagementApiCredentialRepository
{
    public async Task<ManagementApiCredentialEntity?> GetAsync(string applicationKey, string environment, Guid credentialId, CancellationToken ct = default)
    {
        var response = await table.GetEntityIfExistsAsync<CredentialRow>(Partition(applicationKey, environment),
            Row(credentialId), cancellationToken: ct);
        return response.HasValue ? FromRow(response.Value!) : null;
    }

    public async Task<ManagementApiCredentialEntity> CreateAsync(ManagementApiCredentialEntity credential, CancellationToken ct = default)
    {
        var row = ToRow(credential);
        var response = await table.AddEntityAsync(row, ct);
        return credential with { Version = response.Headers.ETag?.ToString() ?? row.ETag.ToString() };
    }

    public async Task<ManagementApiCredentialEntity> ReplaceAsync(ManagementApiCredentialEntity credential, string expectedVersion, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(expectedVersion) || expectedVersion == "*")
            throw new ArgumentException("An explicit version is required.", nameof(expectedVersion));
        try
        {
            var response = await table.UpdateEntityAsync(ToRow(credential), new ETag(expectedVersion), TableUpdateMode.Replace, ct);
            return credential with { Version = response.Headers.ETag?.ToString() ?? "" };
        }
        catch (RequestFailedException ex) when (ex.Status is 404 or 412)
        {
            throw new ManagementCredentialConflictException();
        }
    }

    // JSON array encoding avoids ambiguous app/environment concatenation. Never query across deployments.
    private static string Partition(string applicationKey, string environment)
        => "DEPLOY|" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new[] { applicationKey, environment }))));
    private static string Row(Guid id) => "CRED|" + id.ToString("N");

    private static CredentialRow ToRow(ManagementApiCredentialEntity row) => new()
    {
        PartitionKey = Partition(row.ApplicationKey, row.Environment), RowKey = Row(row.CredentialId),
        CredentialId = row.CredentialId, ApplicationKey = row.ApplicationKey, Environment = row.Environment,
        SecretHash = row.SecretHash, CapabilitiesJson = JsonSerializer.Serialize(row.Capabilities), Status = row.Status,
        CreatedUtc = row.CreatedUtc, ExpiresUtc = row.ExpiresUtc, RotatedUtc = row.RotatedUtc, RevokedUtc = row.RevokedUtc,
        CreatedByOperatorId = row.CreatedByOperatorId, RotatedByOperatorId = row.RotatedByOperatorId, RevokedByOperatorId = row.RevokedByOperatorId
    };

    private static ManagementApiCredentialEntity FromRow(CredentialRow row) => new()
    {
        CredentialId = row.CredentialId, ApplicationKey = row.ApplicationKey, Environment = row.Environment,
        SecretHash = row.SecretHash, Capabilities = Array.AsReadOnly(JsonSerializer.Deserialize<string[]>(row.CapabilitiesJson) ?? []),
        Status = row.Status, CreatedUtc = row.CreatedUtc, ExpiresUtc = row.ExpiresUtc,
        RotatedUtc = row.RotatedUtc, RevokedUtc = row.RevokedUtc, CreatedByOperatorId = row.CreatedByOperatorId,
        RotatedByOperatorId = row.RotatedByOperatorId, RevokedByOperatorId = row.RevokedByOperatorId, Version = row.ETag.ToString()
    };

    // Provider shape stays here, leaving the core entity free from Azure/HTTP dependencies.
    public sealed class CredentialRow : ITableEntity
    {
        public string PartitionKey { get; set; } = "";
        public string RowKey { get; set; } = "";
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
        public Guid CredentialId { get; set; }
        public string ApplicationKey { get; set; } = "";
        public string Environment { get; set; } = "";
        public string SecretHash { get; set; } = "";
        public string CapabilitiesJson { get; set; } = "[]";
        public string Status { get; set; } = "";
        public DateTimeOffset CreatedUtc { get; set; }
        public DateTimeOffset ExpiresUtc { get; set; }
        public DateTimeOffset? RotatedUtc { get; set; }
        public DateTimeOffset? RevokedUtc { get; set; }
        public Guid CreatedByOperatorId { get; set; }
        public Guid? RotatedByOperatorId { get; set; }
        public Guid? RevokedByOperatorId { get; set; }
        public override string ToString() => "Management credential row (secret hash redacted)";
    }
}
