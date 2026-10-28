using System.Text.RegularExpressions;
using IBeam.Management.Operations;

namespace IBeam.Management.TenantData;

/// <summary>Portable metadata projection for both ends of the signed protocol. No app-specific data or artifact reference is representable.</summary>
public static class ManagementTenantExportMetadata
{
    public static bool Key(string? value) => value is { Length: > 0 and <= 64 } && Regex.IsMatch(value, "^[a-z][a-z0-9.-]*$", RegexOptions.CultureInvariant);
    public static ManagementTenantExportResult Project(ManagementTenantExportResult value, Guid tenant, Guid? expectedJob = null)
    {
        if (value is null || tenant == Guid.Empty || value.TenantId != tenant || value.JobId == Guid.Empty || expectedJob.HasValue && value.JobId != expectedJob ||
            value.Outcome is not ("accepted" or "found" or "unknown") || value.Outcome == "unknown" && value.Job is not null || value.Outcome != "unknown" && value.Job is null)
            throw new ManagementOperationUnavailableException();
        if (value.Job is not { } job) return new(tenant, value.JobId, "unknown", null);
        if (job.ContractVersion != 1 || job.TenantId != tenant || job.JobId != value.JobId || !ManagementTenantExportStates.All.Contains(job.State, StringComparer.Ordinal) ||
            job.CreatedUtc.Offset != TimeSpan.Zero || job.UpdatedUtc.Offset != TimeSpan.Zero || job.ExpiresUtc.Offset != TimeSpan.Zero || job.UpdatedUtc < job.CreatedUtc || job.ExpiresUtc <= job.CreatedUtc ||
            job.Consistency is not ("transactional" or "point-in-time" or "best-effort") || job.Coverage is not { Length: > 0 and <= 32 } ||
            job.FailureCode is not (null or "handler-failed" or "storage-unavailable" or "delivery-denied" or "cancelled" or "expired" or "unknown")) throw new ManagementOperationUnavailableException();
        var coverage = job.Coverage.ToArray();
        if (coverage.Any(x => x is null || !Key(x.Key) || x.Kind is not ("dataset" or "attachment") || x.Disposition is not ("pending" or "completed" or "omitted" or "retained")) ||
            coverage.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != coverage.Length ||
            job.State == ManagementTenantExportStates.Succeeded && (coverage.Any(x => x.Disposition != "completed") || job.FailureCode is not null) ||
            job.State == ManagementTenantExportStates.PartiallyCompleted && (!coverage.Any(x => x.Disposition is "omitted" or "retained") || coverage.Any(x => x.Disposition == "pending")))
            throw new ManagementOperationUnavailableException();
        return new(tenant, value.JobId, value.Outcome, new(job.ContractVersion, tenant, value.JobId, job.State, job.CreatedUtc, job.UpdatedUtc, job.ExpiresUtc,
            job.Consistency, coverage.Select(x => new ManagementTenantExportCoverage(x.Key, x.Kind, x.Disposition)).ToArray(), job.FailureCode));
    }
}
