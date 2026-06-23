using System.Collections.Concurrent;

namespace IBeam.Licensing.Services;

public sealed class InMemoryLicensingStore : ILicensingStore
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
        _licenses[(license.TenantId, license.LicenseId)] = license;
        return Task.FromResult(license);
    }

    public Task DeleteLicenseAsync(Guid tenantId, Guid licenseId, CancellationToken ct = default)
    {
        _licenses.TryRemove((tenantId, licenseId), out _);

        foreach (var key in _assignments.Keys.Where(x => x.TenantId == tenantId && x.LicenseId == licenseId).ToList())
            _assignments.TryRemove(key, out _);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LicenseSeatAssignmentInfo>> ListAssignmentsAsync(Guid tenantId, Guid licenseId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<LicenseSeatAssignmentInfo>>(
            _assignments.Values
                .Where(x => x.TenantId == tenantId && x.LicenseId == licenseId)
                .OrderBy(x => x.CreatedUtc)
                .ToList());

    public Task<LicenseSeatAssignmentInfo> AddAssignmentAsync(LicenseSeatAssignmentInfo assignment, CancellationToken ct = default)
    {
        _assignments[(assignment.TenantId, assignment.LicenseId, assignment.AssignmentId)] = assignment;
        return Task.FromResult(assignment);
    }

    public Task DeleteAssignmentAsync(Guid tenantId, Guid licenseId, Guid assignmentId, CancellationToken ct = default)
    {
        _assignments.TryRemove((tenantId, licenseId, assignmentId), out _);
        return Task.CompletedTask;
    }
}
