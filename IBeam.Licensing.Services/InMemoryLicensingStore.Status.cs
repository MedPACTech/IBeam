using System.Collections.Frozen;

namespace IBeam.Licensing.Services;

/// <summary>Synthetic/development provider only. The same lock fences every native licence upsert/delete and status receipt.</summary>
public sealed partial class InMemoryLicensingStore
{
    private readonly object _statusGate = new();
    private readonly HashSet<(Guid, Guid)> _protectedLicenses = [];
    private readonly Dictionary<(Guid, Guid), string?> _resumeStatuses = [];
    private readonly Dictionary<(Guid, Guid, Guid), (string Scope, string Hash, LicenseStatusReceipt Receipt)> _statusReceipts = [];
    public int StatusProtocolVersion => 1;
    public bool NativeWritersParticipate => true;
    public bool DurableStatusReceipts => false;
    public Task<LicenseStatusSnapshot?> ReadStatusAsync(Guid tenantId, Guid licenseId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_statusGate)
        {
            var key = (tenantId, licenseId);
            return Task.FromResult(_licenses.TryGetValue(key, out var row)
                ? new LicenseStatusSnapshot(row, row.Version, _resumeStatuses.GetValueOrDefault(key)) : null);
        }
    }
    public Task<LicenseStatusReceipt?> FindStatusReceiptAsync(Guid tenantId, Guid licenseId, Guid operationId,
        string authorityScope, string transition, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_statusGate)
        {
            if (!_statusReceipts.TryGetValue((tenantId, licenseId, operationId), out var found)) return Task.FromResult<LicenseStatusReceipt?>(null);
            if (found.Scope != authorityScope || found.Receipt.Transition != transition) throw new LicenseStatusConflictException();
            return Task.FromResult<LicenseStatusReceipt?>(found.Receipt);
        }
    }
    public Task<LicenseStatusReceipt> CommitStatusAsync(LicenseStatusCommit commit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var command = commit.Command;
        lock (_statusGate)
        {
            var key = (command.TenantId, command.LicenseId);
            var receiptKey = (command.TenantId, command.LicenseId, command.OperationId);
            if (commit.IntentHash != LicenseStatusFingerprint.Compute(command)) throw new LicenseStatusConflictException();
            if (_statusReceipts.TryGetValue(receiptKey, out var prior))
            {
                if (prior.Scope != command.AuthorityScope || prior.Hash != commit.IntentHash) throw new LicenseStatusConflictException();
                return Task.FromResult(prior.Receipt);
            }
            if (!_licenses.TryGetValue(key, out var current) || current.Version != command.ExpectedVersion) throw new LicenseStatusPreconditionException();
            var updated = current with { Status = commit.Status, Version = Guid.NewGuid().ToString("N") };
            var receipt = new LicenseStatusReceipt(command.TenantId, command.LicenseId, command.OperationId, command.Transition, updated.Status, updated.Version);
            _licenses[key] = updated;
            _resumeStatuses[key] = commit.ResumeStatus;
            _protectedLicenses.Add(key);
            _statusReceipts.Add(receiptKey, (command.AuthorityScope, commit.IntentHash, receipt));
            return Task.FromResult(receipt);
        }
    }
    private static TenantLicenseRecord Freeze(TenantLicenseRecord row) => row with
    {
        Entitlements = Array.AsReadOnly(row.Entitlements.ToArray()),
        Limits = row.Limits.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
        Metadata = row.Metadata.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase)
    };
}
