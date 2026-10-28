using System.Security.Cryptography;
using System.Text.Json;
using IBeam.Management.Operations;
using IBeam.Management.TenantData;

namespace IBeam.Management.SyntheticConsumer;

/// <summary>Private temporary synthetic data only. Not registered in the demo or a production cleanup adapter.</summary>
public sealed class SyntheticTenantPurger : IManagementTenantPurgeHandler, IManagementTenantPurgeAccessFence, IDisposable
{
    private readonly string root, binding, environment;
    public int ProtocolVersion => 1;
    public string Key => "synthetic-files";
    /// <summary>Fault injection after a durable synthetic commit; never configure from a request.</summary>
    public Action<string>? AfterCommitForTesting { get; set; }
    private SyntheticTenantPurger(string directory, string binding, string environment)
        { root = directory; this.binding = binding; this.environment = environment; }
    public static SyntheticTenantPurger CreateNew(ManagementTenantPurgeExecution scope)
    {
        Guard(scope);
        var root = Path.Combine(Path.GetTempPath(), "ibeam-synthetic-purge-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(root)) throw new ManagementOperationUnavailableException();
        Directory.CreateDirectory(root);
        var result = new SyntheticTenantPurger(root, Scope(scope), scope.Environment);
        using var gate = result.Lock();
        result.WriteState(new(result.binding, false, null, []));
        File.WriteAllText(result.PathFor("app-datasets.json"), "PRIVATE-SYNTHETIC-FACILITIES-SETTINGS-CHATS-REMOVED-UNSEATED");
        File.WriteAllBytes(result.PathFor("audio.bin"), [1, 2, 3, 4]);
        File.WriteAllText(result.PathFor("backup.json"), "PRIVATE-SYNTHETIC-RETAINED-BACKUP");
        return result;
    }
    public SyntheticTenantPurger Reopen() { CheckRoot(); return new(root, binding, environment); }
    public ManagementTenantPurgeDefinition Describe() => new(1, "synthetic-files", 1, Array.AsReadOnly(new[] {
        new ManagementTenantPurgeCategory("app-datasets", "dataset", "delete"), new("audio-files", "attachment", "delete"),
        new("app-purge-controls", "identity-metadata", "retain"), new("backups", "backup", "retain"),
        new("external-systems", "external", "exclude"), new("global-accounts", "identity-metadata", "exclude") }));
    public Task PreviewAsync(ManagementTenantPurgeExecution e, CancellationToken ct = default)
    {
        Check(e); using var gate = Lock(); var state = ReadState();
        if (state.Blocked) throw new ManagementOperationPreconditionException();
        return Task.CompletedTask;
    }
    public async Task FenceOrReconcileAsync(ManagementTenantPurgeExecution e, string expectedVersion, CancellationToken ct = default)
    {
        Check(e); if (e.Authority.ResourceVersion != expectedVersion || string.IsNullOrWhiteSpace(expectedVersion)) throw new ManagementOperationPreconditionException();
        // The journal-only proof avoids recursive validation of this fence while acquiring it.
        await Lease(e, journalOnly: true, ct); using var gate = Lock(); var state = ReadState(); var owner = Job(e);
        if (state.Blocked) { if (state.Owner != owner) throw new ManagementOperationConflictException(); return; }
        await Lease(e, journalOnly: true, ct);
        WriteState(state with { Blocked = true, Owner = owner }); AfterCommitForTesting?.Invoke("app-fence");
    }
    public Task RequireFencedAsync(ManagementTenantPurgeExecution e, CancellationToken ct = default)
    {
        Check(e); using var gate = Lock(); RequireOwner(ReadState(), e); return Task.CompletedTask;
    }
    public async Task PurgeOrReconcileAsync(ManagementTenantPurgeExecution e, CancellationToken ct = default)
    {
        Check(e);
        foreach (var (key, file) in new[] { ("app-datasets", "app-datasets.json"), ("audio-files", "audio.bin") }) {
            await Lease(e, false, ct); using var gate = Lock(); var state = ReadState(); RequireOwner(state, e);
            if (state.Completed.Contains(key, StringComparer.Ordinal)) continue;
            await Lease(e, true, ct); // State is held under the same app lock as every ordinary writer.
            File.Delete(PathFor(file));
            // A crash between deletion and checkpoint is reconciled by the same file's absence, never another tenant/job.
            WriteState(state with { Completed = [..state.Completed, key] }); AfterCommitForTesting?.Invoke(key);
        }
    }
    public async Task<IReadOnlyList<ManagementTenantPurgeCategory>> VerifyAsync(ManagementTenantPurgeExecution e, CancellationToken ct = default)
    {
        Check(e); await Lease(e, false, ct); using var gate = Lock(); var state = ReadState(); RequireOwner(state, e);
        if (File.Exists(PathFor("app-datasets.json")) || File.Exists(PathFor("audio.bin")) || state.Completed.Length != 2)
            throw new ManagementTenantPurgePermanentFailureException("verification-failed");
        AfterCommitForTesting?.Invoke("verification");
        return Describe().Categories.Select(x => x with { Disposition = x.Disposition switch { "delete" => "deleted", "retain" => "retained", _ => "excluded" } }).ToArray();
    }
    /// <summary>Illustrates the same app lock/guard required on ordinary writes and customer request/refresh admission.</summary>
    public void CustomerWriteOrAccess()
    {
        using var gate = Lock(); if (ReadState().Blocked) throw new ManagementAccessDeniedException();
        File.WriteAllText(PathFor("app-datasets.json"), "PRIVATE-SYNTHETIC-UPDATED");
    }
    public bool DataExists => File.Exists(PathFor("app-datasets.json")) || File.Exists(PathFor("audio.bin"));
    public bool BackupExists => File.Exists(PathFor("backup.json"));
    public int CompletedSteps { get { using var gate = Lock(); return ReadState().Completed.Length; } }
    private static async Task Lease(ManagementTenantPurgeExecution e, bool journalOnly, CancellationToken ct)
    {
        if (e.LeaseFence is not { JournalProtocolVersion: 1 } lease) throw new ManagementAccessDeniedException();
        if (journalOnly) await lease.RequireJournalAsync(ct); else await lease.RequireAsync(ct);
    }
    private void Check(ManagementTenantPurgeExecution e)
    { Guard(e); if (e.Environment != environment || Scope(e) != binding) throw new ManagementAccessDeniedException(); CheckRoot(); }
    private static void Guard(ManagementTenantPurgeExecution e)
    {
        var a = e.Authority;
        if (e.Environment is not ("Development" or "Testing") || e.ApplicationKey != "fixture" || a.OrganizationId == Guid.Empty || a.ApplicationId == Guid.Empty ||
            a.RealmId == Guid.Empty || a.TenantId == Guid.Empty || a.ActorUserId == Guid.Empty || a.ResourceId != a.TenantId)
            throw new ManagementAccessDeniedException();
    }
    private static string Scope(ManagementTenantPurgeExecution e) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
        e.ApplicationKey, e.Environment, e.Authority.OrganizationId, e.Authority.ApplicationId, e.Authority.RealmId, e.Authority.TenantId, e.Authority.ActorUserId })));
    private static string Job(ManagementTenantPurgeExecution e) => e.JobId != Guid.Empty && e.JobId == e.Authority.OperationId ?
        Scope(e) + ":" + e.JobId.ToString("D") : throw new ManagementAccessDeniedException();
    private static void RequireOwner(State state, ManagementTenantPurgeExecution e)
    { if (!state.Blocked || state.Owner != Job(e)) throw new ManagementAccessDeniedException(); }
    private void CheckRoot()
    {
        var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        if (Path.GetDirectoryName(root) != parent || !Path.GetFileName(root).StartsWith("ibeam-synthetic-purge-", StringComparison.Ordinal) ||
            !Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new ManagementAccessDeniedException();
    }
    private string PathFor(string name)
    {
        CheckRoot(); var path = Path.Combine(root, name);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new ManagementAccessDeniedException();
        return path;
    }
    private FileStream Lock()
    {
        try { return new(PathFor("guard.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new ManagementTenantPurgePendingException(); }
    }
    private State ReadState()
    {
        var state = JsonSerializer.Deserialize<State>(File.ReadAllBytes(PathFor("state.json"))) ?? throw new ManagementOperationUnavailableException();
        if (state.Binding != binding) throw new ManagementAccessDeniedException(); return state;
    }
    private void WriteState(State state)
    {
        var temporary = PathFor("state-" + Guid.NewGuid().ToString("N") + ".tmp");
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(stream, state); stream.Flush(true); }
        File.Move(temporary, PathFor("state.json"), overwrite: true);
    }
    public void Dispose()
    {
        if (!Directory.Exists(root)) return;
        CheckRoot(); using (var gate = Lock()) _ = ReadState();
        // This exact constructor-owned temporary directory was checked above; no caller-supplied target is accepted.
        Directory.Delete(root, recursive: true);
    }
    private sealed record State(string Binding, bool Blocked, string? Owner, string[] Completed);
}
