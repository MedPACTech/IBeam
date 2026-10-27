using System.Text.RegularExpressions;
using IBeam.Management.Operations;
using IBeam.Management.TenantData;

namespace IBeam.Management.TenantData.Target.Services;

/// <summary>Reconstruct bounded approved metadata; never serialize an app's arbitrary result object.</summary>
public static class ManagementTenantExportProtocol
{
    public static bool Key(string? value) => value is { Length: > 0 and <= 64 } && Regex.IsMatch(value, "^[a-z][a-z0-9.-]*$", RegexOptions.CultureInvariant);
    public static ManagementTenantExportDefinition Definition(ManagementTenantExportDefinition source)
    {
        if (source is null || source.ContractVersion != 1 || !Key(source.HandlerKey) || source.InventoryVersion < 1 ||
            source.Consistency is not ("transactional" or "point-in-time" or "best-effort") || source.Datasets is not { Count: > 0 and <= 32 })
            throw new ManagementOperationUnavailableException();
        var items = source.Datasets.ToArray();
        if (items.Any(x => x is null || !Key(x.Key) || x.Kind is not ("dataset" or "attachment")) || items.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != items.Length)
            throw new ManagementOperationUnavailableException();
        return source with { Datasets = Array.AsReadOnly(items.Select(x => new ManagementTenantExportDataset(x.Key, x.Kind)).ToArray()) };
    }
    public static ManagementTenantExportResult Result(ManagementTenantExportResult source, Guid tenant, Guid job, ManagementTenantExportDefinition inventory)
    {
        if (source is null || source.TenantId != tenant || source.JobId != job || source.Outcome is not ("accepted" or "found" or "unknown") ||
            source.Outcome == "unknown" && source.Job is not null || source.Outcome != "unknown" && source.Job is null)
            throw new ManagementOperationUnavailableException();
        if (source.Job is not { } status) return new(tenant, job, "unknown", null);
        var definition = Definition(inventory);
        if (status.ContractVersion != 1 || status.TenantId != tenant || status.JobId != job || !ManagementTenantExportStates.All.Contains(status.State, StringComparer.Ordinal) ||
            status.CreatedUtc.Offset != TimeSpan.Zero || status.UpdatedUtc.Offset != TimeSpan.Zero || status.ExpiresUtc.Offset != TimeSpan.Zero ||
            status.UpdatedUtc < status.CreatedUtc || status.ExpiresUtc <= status.CreatedUtc || status.Consistency != definition.Consistency ||
            status.Coverage is null || status.Coverage.Length != definition.Datasets.Count ||
            status.FailureCode is not (null or "handler-failed" or "storage-unavailable" or "delivery-denied" or "cancelled" or "expired" or "unknown"))
            throw new ManagementOperationUnavailableException();
        var coverage = status.Coverage.ToArray();
        if (coverage.Any(x => x is null || !definition.Datasets.Any(d => d.Key == x.Key && d.Kind == x.Kind) ||
                x.Disposition is not ("pending" or "completed" or "omitted" or "retained")) ||
            coverage.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != coverage.Length ||
            status.State == ManagementTenantExportStates.Succeeded && (coverage.Any(x => x.Disposition != "completed") || status.FailureCode is not null) ||
            status.State == ManagementTenantExportStates.PartiallyCompleted && (!coverage.Any(x => x.Disposition is "omitted" or "retained") || coverage.Any(x => x.Disposition == "pending")))
            throw new ManagementOperationUnavailableException();
        return new(tenant, job, source.Outcome, status with { Coverage = coverage.Select(x => new ManagementTenantExportCoverage(x.Key, x.Kind, x.Disposition)).ToArray() });
    }
}
