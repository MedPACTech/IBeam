using System.Collections.Frozen;

namespace IBeam.Licensing.Services;

public sealed partial class InMemoryLicensingStore
{
    private readonly HashSet<(Guid, Guid)> _protectedSeatLicenses = [];
    private readonly Dictionary<(Guid, Guid, Guid), (string Scope, string Hash, LicenseSeatReceipt Receipt)> _seatReceipts = [];
    public int SeatProtocolVersion => 1;
    public bool NativeSeatWritersParticipate => true;
    public bool DurableSeatReceipts => false;
    public Task<bool> RequiresSeatProtectionAsync(Guid tenantId, Guid licenseId, CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); lock (_statusGate) return Task.FromResult(_protectedSeatLicenses.Contains((tenantId, licenseId))); }
    public Task<LicenseSeatSnapshot?> ReadSeatsAsync(Guid tenantId, Guid licenseId, int maximumAssignments, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (maximumAssignments is < 1 or > LicenseSeatProtocol.MaximumAssignments) throw new LicenseSeatUnavailableException();
        lock (_statusGate)
        {
            var key = (tenantId, licenseId);
            if (!_licenses.TryGetValue(key, out var license)) return Task.FromResult<LicenseSeatSnapshot?>(null);
            var assignments = _assignments.Values.Where(x => x.TenantId == tenantId && x.LicenseId == licenseId).Take(maximumAssignments + 1).ToArray();
            if (assignments.Length > maximumAssignments) throw new LicenseSeatUnavailableException();
            return Task.FromResult<LicenseSeatSnapshot?>(new(license, license.Version, Array.AsReadOnly(assignments), _protectedSeatLicenses.Contains(key)));
        }
    }
    public Task<LicenseSeatReceipt?> FindSeatReceiptAsync(Guid tenantId, Guid licenseId, Guid operationId, string scope, string transition, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_statusGate)
        {
            if (!_seatReceipts.TryGetValue((tenantId, licenseId, operationId), out var prior)) return Task.FromResult<LicenseSeatReceipt?>(null);
            if (prior.Scope != scope || prior.Receipt.Transition != transition) throw new LicenseSeatConflictException();
            return Task.FromResult<LicenseSeatReceipt?>(prior.Receipt);
        }
    }
    public Task<LicenseSeatReceipt> CommitSeatAsync(LicenseSeatCommit commit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); var command = commit.Command;
        lock (_statusGate)
        {
            var key = (command.TenantId, command.LicenseId); var receiptKey = (command.TenantId, command.LicenseId, command.OperationId);
            if (commit.IntentHash != LicenseSeatFingerprint.Compute(command)) throw new LicenseSeatConflictException();
            if (_seatReceipts.TryGetValue(receiptKey, out var prior))
            {
                if (prior.Scope != command.AuthorityScope || prior.Hash != commit.IntentHash) throw new LicenseSeatConflictException();
                return Task.FromResult(prior.Receipt);
            }
            var assignmentId = commit.Assignment?.AssignmentId ?? command.AssignmentId ?? throw new LicenseSeatConflictException();
            if (assignmentId == Guid.Empty || command.Transition == "assign" && (commit.Assignment is null || commit.RemoveAssignmentId.HasValue ||
                    !LicenseSeatFingerprint.MatchesUser(commit.Assignment, command.UserId)) ||
                command.Transition == "unassign" && (commit.Assignment is not null || commit.RemoveAssignmentId != command.AssignmentId)) throw new LicenseSeatConflictException();
            ApplySeat(command.TenantId, command.LicenseId, command.ExpectedVersion, commit.Assignment, commit.RemoveAssignmentId);
            _protectedSeatLicenses.Add(key); _protectedLicenses.Add(key);
            var receipt = new LicenseSeatReceipt(command.TenantId, command.LicenseId, command.UserId, assignmentId,
                command.OperationId, command.Transition, commit.Disposition, _licenses[key].Version);
            _seatReceipts.Add(receiptKey, (command.AuthorityScope, commit.IntentHash, receipt));
            return Task.FromResult(receipt);
        }
    }
    public Task ApplyNativeSeatAsync(Guid tenantId, Guid licenseId, string expectedVersion, LicenseSeatAssignmentInfo? assignment, Guid? removeAssignmentId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_statusGate) { ApplySeat(tenantId, licenseId, expectedVersion, assignment, removeAssignmentId); return Task.CompletedTask; }
    }
    private void ApplySeat(Guid tenantId, Guid licenseId, string expectedVersion, LicenseSeatAssignmentInfo? assignment, Guid? removeId)
    {
        if (!_licenses.TryGetValue((tenantId, licenseId), out var license) || license.Version != expectedVersion) throw new LicenseSeatPreconditionException();
        if (assignment is not null && (assignment.TenantId != tenantId || assignment.LicenseId != licenseId || assignment.AssignmentId == Guid.Empty) ||
            assignment is not null && removeId.HasValue) throw new LicenseSeatConflictException();
        var changed = false;
        if (assignment is not null)
        {
            var key = (tenantId, licenseId, assignment.AssignmentId);
            if (_assignments.TryGetValue(key, out var current))
            {
                if (current != assignment) throw new LicenseSeatConflictException();
            }
            else
            {
                if (_assignments.Values.Count(x => x.TenantId == tenantId && x.LicenseId == licenseId) >= LicenseSeatProtocol.MaximumAssignments)
                    throw new LicenseSeatUnavailableException();
                _assignments[key] = FreezeAssignment(assignment); changed = true;
            }
        }
        if (removeId.HasValue) changed = _assignments.TryRemove((tenantId, licenseId, removeId.Value), out _);
        if (changed) AdvanceLicense(tenantId, licenseId);
    }
    private void AdvanceLicense(Guid tenantId, Guid licenseId)
    {
        if (_licenses.TryGetValue((tenantId, licenseId), out var license))
            _licenses[(tenantId, licenseId)] = license with { Version = Guid.NewGuid().ToString("N") };
    }
    private static LicenseSeatAssignmentInfo FreezeAssignment(LicenseSeatAssignmentInfo assignment)
        => assignment with { Metadata = assignment.Metadata.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase) };
}
