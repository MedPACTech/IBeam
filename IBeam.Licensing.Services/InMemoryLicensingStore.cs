using System.Collections.Concurrent;

namespace IBeam.Licensing.Services;

public sealed partial class InMemoryLicensingStore : ILicensingStore, ILicenseStatusConditionalStore, ILicenseSeatConditionalStore
{
    private readonly ConcurrentDictionary<(Guid TenantId, Guid LicenseId), TenantLicenseRecord> _licenses = [];
    private readonly ConcurrentDictionary<(Guid TenantId, Guid LicenseId, Guid AssignmentId), LicenseSeatAssignmentInfo> _assignments = [];

    public Task<IReadOnlyList<TenantLicenseRecord>> ListLicensesAsync(Guid tenantId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TenantLicenseRecord>>(
            _licenses.Values
                .Where(x => x.TenantId == tenantId)
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList());

    public Task<TenantLicenseRecord?> GetLicenseAsync(Guid tenantId, Guid licenseId, CancellationToken ct = default)
    {
        _licenses.TryGetValue((tenantId, licenseId), out var license);
        return Task.FromResult(license);
    }

    public Task<TenantLicenseRecord> UpsertLicenseAsync(TenantLicenseRecord license, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_statusGate)
        {
            var key = (license.TenantId, license.LicenseId);
            if (_protectedLicenses.Contains(key) && (!_licenses.TryGetValue(key, out var existing) || existing.Version != license.Version))
                throw new LicenseStatusPreconditionException();
            var saved = Freeze(license with { Version = Guid.NewGuid().ToString("N") });
            _licenses[key] = saved;
            return Task.FromResult(saved);
        }
    }

    public Task DeleteLicenseAsync(Guid tenantId, Guid licenseId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_statusGate)
        {
            if (_protectedLicenses.Contains((tenantId, licenseId))) throw new LicenseStatusConflictException();
            _licenses.TryRemove((tenantId, licenseId), out _);
            foreach (var key in _assignments.Keys.Where(x => x.TenantId == tenantId && x.LicenseId == licenseId).ToList())
                _assignments.TryRemove(key, out _);
            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyList<LicenseSeatAssignmentInfo>> ListAssignmentsAsync(Guid tenantId, Guid licenseId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<LicenseSeatAssignmentInfo>>(
            _assignments.Values
                .Where(x => x.TenantId == tenantId && x.LicenseId == licenseId)
                .OrderBy(x => x.CreatedUtc)
                .ToList());

    public Task<LicenseSeatAssignmentInfo> AddAssignmentAsync(LicenseSeatAssignmentInfo assignment, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_statusGate)
        {
            if (_protectedSeatLicenses.Contains((assignment.TenantId, assignment.LicenseId))) throw new LicenseSeatPreconditionException();
            var saved = FreezeAssignment(assignment);
            _assignments[(assignment.TenantId, assignment.LicenseId, assignment.AssignmentId)] = saved;
            AdvanceLicense(assignment.TenantId, assignment.LicenseId);
            return Task.FromResult(saved);
        }
    }

    public Task DeleteAssignmentAsync(Guid tenantId, Guid licenseId, Guid assignmentId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_statusGate)
        {
            if (_protectedSeatLicenses.Contains((tenantId, licenseId))) throw new LicenseSeatPreconditionException();
            if (_assignments.TryRemove((tenantId, licenseId, assignmentId), out _)) AdvanceLicense(tenantId, licenseId);
            return Task.CompletedTask;
        }
    }
}
