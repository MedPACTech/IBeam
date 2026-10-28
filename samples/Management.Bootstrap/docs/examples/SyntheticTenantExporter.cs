using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using IBeam.Management.Operations;
using IBeam.Management.TenantData;

namespace IBeam.Management.SyntheticConsumer;

/// <summary>Development/Testing consumer example only. Business bytes and durable provider receipts remain in a private app-owned directory.</summary>
public sealed class SyntheticTenantExporter : IManagementTenantExportHandler, IManagementTenantExportDelivery,
    IManagementTenantExportCustomerAuthorization, IManagementTenantExportPolicy
{
    private readonly string root;
    private readonly Guid realm, recipient;
    private readonly TimeProvider clock;
    public bool CustomerEnabled { get; set; } = true;
    public bool Available { get; set; } = true;
    public int ProtocolVersion => 1;
    public SyntheticTenantExporter(string privateDirectory, string environment, Guid realmId, Guid approvedCustomerId, TimeProvider? time = null)
    {
        if (environment is not ("Development" or "Testing") || realmId == Guid.Empty || approvedCustomerId == Guid.Empty)
            throw new InvalidOperationException("The synthetic export provider requires an explicit isolated Development/Testing realm and customer.");
        root = Path.GetFullPath(privateDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        realm = realmId; recipient = approvedCustomerId; clock = time ?? TimeProvider.System;
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(root);
        else Directory.CreateDirectory(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    public ManagementTenantExportDefinition Describe() => new(1, "synthetic-consumer", 1, "point-in-time", Array.AsReadOnly(new[] {
        new ManagementTenantExportDataset("facilities", "dataset"), new("settings", "dataset"), new("commercial", "dataset"), new("audit-history", "dataset"),
        new("active-member-chats", "dataset"), new("removed-member-chats", "dataset"), new("unseated-member-chats", "dataset"), new("audio", "attachment"), new("attachments", "attachment") }));
    public Task RequireAsync(ManagementTenantExportExecution e, string version, CancellationToken ct = default)
    {
        if (e.Authority.RealmId != realm || e.PolicyKey is not ("enterprise" or "partial") || version != "synthetic-v1") throw new ManagementOperationPreconditionException();
        return Task.CompletedTask;
    }
    public Task RequireStatusAsync(ManagementTenantExportExecution e, CancellationToken ct = default)
    { if (e.Authority.RealmId != realm || !CustomerEnabled) throw new ManagementAccessDeniedException(); return Task.CompletedTask; }

    public async Task<ManagementTenantExportOutput> ExecuteOrResumeAsync(ManagementTenantExportExecution e, CancellationToken ct = default)
    {
        Check(); await Fence(e, ct); var directory = DirectoryFor(e); Directory.CreateDirectory(directory);
        FileStream lease;
        try { lease = new FileStream(Path.Combine(directory, "provider.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new ManagementTenantExportPendingException(); }
        using (lease)
        {
            var path = Path.Combine(directory, "bundle.json");
            Bundle bundle;
            if (File.Exists(path)) bundle = JsonSerializer.Deserialize<Bundle>(await File.ReadAllBytesAsync(path, ct)) ?? throw new ManagementOperationUnavailableException();
            else
            {
                var data = Describe().Datasets.Where(x => e.PolicyKey != "partial" || x.Key != "commercial")
                    .ToDictionary(x => x.Key, x => new[] { "PRIVATE-SYNTHETIC-" + x.Key, "PRIVATE-HISTORY-INCLUDES-REMOVED-AND-UNSEATED" }, StringComparer.Ordinal);
                bundle = new(ScopeKey(e), e.Authority.TenantId, e.ArtifactExpiresUtc ?? throw new ManagementOperationInvalidException(), data);
                await WriteOnce(path, bundle, e, ct);
            }
            if (bundle.Scope != ScopeKey(e) || bundle.TenantId != e.Authority.TenantId || bundle.ExpiresUtc <= clock.GetUtcNow()) throw new ManagementAccessDeniedException();
            var coverage = Describe().Datasets.Select(x => new ManagementTenantExportCoverage(x.Key, x.Kind, bundle.Data.ContainsKey(x.Key) ? "completed" : "retained")).ToArray();
            await Fence(e, ct);
            return new(new("synthetic-export:" + ScopeKey(e), bundle.ExpiresUtc), coverage);
        }
    }
    public async Task DeliverOrReconcileAsync(ManagementTenantExportExecution e, ManagementTenantExportArtifact artifact, CancellationToken ct = default)
    {
        await Fence(e, ct); await RequireArtifactAsync(e, artifact, ct);
        if (!CustomerEnabled || artifact.ExpiresUtc <= clock.GetUtcNow()) throw new ManagementTenantExportPermanentFailureException("delivery-denied");
        // This durable app receipt represents delivery to its approved customer channel.
        // No URL, email or support principal is accepted as an alternate recipient.
        var path = Path.Combine(DirectoryFor(e), "delivered.json");
        if (File.Exists(path))
        {
            var receipt = JsonSerializer.Deserialize<Delivery>(await File.ReadAllBytesAsync(path, ct));
            if (receipt?.Scope != ScopeKey(e) || receipt.CustomerId != recipient) throw new ManagementTenantExportPermanentFailureException("delivery-denied");
            return;
        }
        await WriteOnce(path, new Delivery(ScopeKey(e), recipient), e, ct);
    }
    public Task RequireArtifactAsync(ManagementTenantExportExecution e, ManagementTenantExportArtifact artifact, CancellationToken ct = default)
    {
        Check();
        if (e.Authority.RealmId != realm || artifact.Reference != "synthetic-export:" + ScopeKey(e)) throw new ManagementAccessDeniedException();
        _ = DirectoryFor(e); return Task.CompletedTask;
    }
    public async Task CancelAsync(ManagementTenantExportExecution e, CancellationToken ct = default)
    { Check(); await Fence(e, ct); /* The immutable fixture has no external background computation to cancel. */ }
    public async Task RevokeAndDeleteAsync(ManagementTenantExportExecution e, ManagementTenantExportArtifact artifact, CancellationToken ct = default)
    {
        await Fence(e, ct); await RequireArtifactAsync(e, artifact, ct);
        var directory = DirectoryFor(e); // Resolved absolute hash directory is verified inside the private root before recursive deletion.
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
    public Task RequireAsync(Guid realmId, Guid tenantId, Guid jobId, ClaimsPrincipal customer, CancellationToken ct = default)
    {
        if (!CustomerEnabled || realmId != realm || jobId == Guid.Empty || !customer.Identities.Any(x => x.IsAuthenticated) ||
            !customer.HasClaim("synthetic-customer", recipient.ToString("D")) || !customer.HasClaim("synthetic-tenant", tenantId.ToString("D"))) throw new ManagementAccessDeniedException();
        return Task.CompletedTask;
    }
    /// <summary>The app's customer route calls the framework customer gate first, then this app-owned reader with the authenticated customer.</summary>
    public async Task<byte[]> ReadForCustomerAsync(ManagementTenantExportExecution e, ManagementTenantExportArtifact artifact, ClaimsPrincipal customer, CancellationToken ct = default)
    {
        await RequireAsync(e.Authority.RealmId, e.Authority.TenantId, e.JobId, customer, ct); await RequireArtifactAsync(e, artifact, ct);
        if (artifact.ExpiresUtc <= clock.GetUtcNow()) throw new ManagementAccessDeniedException();
        var directory = DirectoryFor(e);
        if (!File.Exists(Path.Combine(directory, "delivered.json"))) throw new ManagementAccessDeniedException();
        var bytes = await File.ReadAllBytesAsync(Path.Combine(directory, "bundle.json"), ct);
        var bundle = JsonSerializer.Deserialize<Bundle>(bytes) ?? throw new ManagementOperationUnavailableException();
        if (bundle.Scope != ScopeKey(e) || bundle.TenantId != e.Authority.TenantId || bundle.ExpiresUtc <= clock.GetUtcNow()) throw new ManagementAccessDeniedException();
        await RequireAsync(e.Authority.RealmId, e.Authority.TenantId, e.JobId, customer, ct); return bytes;
    }
    private async Task WriteOnce<T>(string path, T value, ManagementTenantExportExecution e, CancellationToken ct)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, JsonSerializer.SerializeToUtf8Bytes(value), ct);
            await Fence(e, ct); File.Move(temporary, path, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private string DirectoryFor(ManagementTenantExportExecution e)
    {
        var path = Path.GetFullPath(Path.Combine(root, ScopeKey(e)));
        if (!path.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new ManagementAccessDeniedException();
        return path;
    }
    private static string ScopeKey(ManagementTenantExportExecution e) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
        e.ApplicationKey, e.Environment, e.JobId, e.Authority.OrganizationId, e.Authority.ApplicationId, e.Authority.RealmId, e.Authority.TenantId, e.Authority.ActorUserId })));
    private static Task Fence(ManagementTenantExportExecution e, CancellationToken ct) => e.LeaseFence?.RequireAsync(ct) ?? throw new ManagementAccessDeniedException();
    private void Check() { if (!Available) throw new IOException("Synthetic artifact storage is unavailable."); }
    private sealed record Bundle(string Scope, Guid TenantId, DateTimeOffset ExpiresUtc, Dictionary<string, string[]> Data)
    { public override string ToString() => "Private synthetic tenant bundle (content redacted)"; }
    private sealed record Delivery(string Scope, Guid CustomerId);
}
