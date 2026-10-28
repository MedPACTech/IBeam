using System.Security.Cryptography;
using System.Text.Json;
using IBeam.Management.Operations;
using IBeam.Management.TenantData;

namespace IBeam.Management.TenantData.Target.Services;

public sealed class ManagementTenantExportJobOptions
{
    public const string SectionName = "IBeam:Management:TenantExport:Jobs";
    public string ActiveKeyId { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public Dictionary<string, string> ArtifactProtectionKeys { get; set; } = [];
    public int RetentionMinutes { get; set; } = 60;
    public int LeaseSeconds { get; set; } = 45;
    public bool EnableBackgroundWorker { get; set; }
    public int ScanIntervalSeconds { get; set; } = 10;
    public override string ToString() => "Tenant export job options (protection keys redacted)";
    public bool IsValid() => RetentionMinutes is >= 1 and <= 1440 && LeaseSeconds is >= 10 and <= 120 && ScanIntervalSeconds is >= 1 and <= 300 &&
        ManagementTenantExportProtocol.Key(ActiveKeyId) && ArtifactProtectionKeys is { Count: > 0 and <= 5 } && ArtifactProtectionKeys.ContainsKey(ActiveKeyId) &&
        ArtifactProtectionKeys.All(x => ManagementTenantExportProtocol.Key(x.Key) && ValidKey(x.Value));
    private static bool ValidKey(string value) { try { return Convert.FromBase64String(value).Length == 32; } catch { return false; } }
}
internal sealed class ManagementTenantExportArtifactProtection(ManagementTenantExportJobOptions options)
{
    private static byte[] Binding(ManagementTenantExportJobEntity row) => JsonSerializer.SerializeToUtf8Bytes(new {
        row.Execution.ApplicationKey, row.Execution.Environment, row.Execution.JobId, row.Execution.Authority.OrganizationId,
        row.Execution.Authority.ApplicationId, row.Execution.Authority.RealmId, row.Execution.Authority.TenantId, row.Execution.Authority.ActorUserId,
        row.Execution.PolicyKey, row.Execution.InventoryVersion, row.Definition, row.CreatedUtc, row.ExpiresUtc, row.IntentHash });
    public string Protect(ManagementTenantExportJobEntity row, ManagementTenantExportArtifact artifact)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new PrivateArtifact(artifact.Reference, artifact.ExpiresUtc));
        if (body.Length > 12000) throw new ManagementOperationUnavailableException();
        var nonce = RandomNumberGenerator.GetBytes(12); var cipher = new byte[body.Length]; var tag = new byte[16];
        using var aes = new AesGcm(Convert.FromBase64String(options.ArtifactProtectionKeys[options.ActiveKeyId]), 16);
        aes.Encrypt(nonce, body, cipher, tag, Binding(row));
        return options.ActiveKeyId + "." + Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }
    public ManagementTenantExportArtifact Unprotect(ManagementTenantExportJobEntity row)
    {
        try
        {
            var separator = row.ProtectedArtifact.LastIndexOf('.');
            if (separator < 1 || !options.ArtifactProtectionKeys.TryGetValue(row.ProtectedArtifact[..separator], out var encodedKey)) throw new ManagementOperationUnavailableException();
            var bytes = Convert.FromBase64String(row.ProtectedArtifact[(separator + 1)..]); if (bytes.Length is < 29 or > 16384) throw new ManagementOperationUnavailableException();
            var body = new byte[bytes.Length - 28]; using var aes = new AesGcm(Convert.FromBase64String(encodedKey), 16);
            aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), body, Binding(row));
            var artifact = JsonSerializer.Deserialize<PrivateArtifact>(body) ?? throw new ManagementOperationUnavailableException();
            return new(artifact.Reference, artifact.ExpiresUtc);
        }
        catch { throw new ManagementOperationUnavailableException(); }
    }
    private sealed record PrivateArtifact(string Reference, DateTimeOffset ExpiresUtc)
    { public override string ToString() => "Private artifact recovery (redacted)"; }
}
