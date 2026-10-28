using IBeam.Management.Operations;

namespace IBeam.Management.TenantData;

/// <summary>Transport-independent reconstruction of bounded operational metadata, never customer data.</summary>
public static class ManagementTenantPurgeMetadata
{
    public static bool Version(string? value) => value is { Length: > 0 and <= 256 } && value.Trim() == value && value != "*" && !value.Any(char.IsControl);
    public static bool Digest(string? value) => value is { Length: 64 } && value.All(x => x is >= '0' and <= '9' or >= 'A' and <= 'F');
    public static ManagementTenantPurgeResult Project(ManagementTenantPurgeResult value, Guid tenant, Guid? operation = null)
    {
        if (value is null || tenant == Guid.Empty || value.TenantId != tenant || value.OperationId == Guid.Empty || operation.HasValue && value.OperationId != operation ||
            value.Outcome is not ("planned" or "accepted" or "found" or "unknown")) Fail();
        if (value!.Outcome == "unknown") {
            if (value.Plan is not null || value.Job is not null) Fail();
            return new(tenant, value.OperationId, "unknown", null, null);
        }
        if (value.Outcome == "planned") {
            var p = value.Plan;
            if (value.Job is not null || p is null || p.ContractVersion != 1 || p.TenantId != tenant || p.PlanId != value.OperationId ||
                !ManagementTenantExportMetadata.Key(p.PolicyKey) || !Version(p.ExpectedVersion) || !Digest(p.InventoryFingerprint) || !Digest(p.ConfirmationBinding) ||
                p.CreatedUtc.Offset != TimeSpan.Zero || p.ExpiresUtc.Offset != TimeSpan.Zero || p.ExpiresUtc <= p.CreatedUtc || p.ExpiresUtc - p.CreatedUtc > TimeSpan.FromMinutes(5)) Fail();
            var categories = Categories(p!.Categories, plan: true);
            if (!categories.Any(x => x.Disposition == "delete")) Fail();
            return new(tenant, value.OperationId, "planned", p with { Categories = categories }, null);
        }
        var job = value.Job;
        if (value.Plan is not null || job is null || job.ContractVersion != 1 || job.TenantId != tenant || job.JobId != value.OperationId || job.PlanId == Guid.Empty || job.PlanId == job.JobId ||
            job.State is not ("requested" or "fencing" or "purging" or "verifying" or "partially-completed" or "succeeded" or "failed" or "cancelled" or "unknown") ||
            job.CreatedUtc.Offset != TimeSpan.Zero || job.UpdatedUtc.Offset != TimeSpan.Zero || job.UpdatedUtc < job.CreatedUtc ||
            job.FailureCode is not (null or "fence-failed" or "handler-failed" or "verification-failed" or "storage-unavailable" or "cancelled" or "unknown")) Fail();
        var coverage = Categories(job!.Coverage, plan: false);
        if (job.State == "succeeded" && (job.FailureCode is not null || coverage.Any(x => x.Disposition == "pending")) ||
            job.State == "partially-completed" && (coverage.Any(x => x.Disposition == "pending") || !coverage.Any(x => x.Disposition == "retained"))) Fail();
        return new(tenant, value.OperationId, value.Outcome, null, job with { Coverage = coverage });
    }
    private static ManagementTenantPurgeCategory[] Categories(ManagementTenantPurgeCategory[] values, bool plan)
    {
        if (values is not { Length: > 1 and <= 32 } || values.Any(x => x is null || !ManagementTenantExportMetadata.Key(x.Key) ||
            x.Kind is not ("dataset" or "attachment" or "identity-metadata" or "external" or "backup") ||
            (plan ? x.Disposition is not ("delete" or "retain" or "exclude") : x.Disposition is not ("pending" or "deleted" or "retained" or "excluded"))) ||
            values.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != values.Length ||
            !values.Any(x => x.Key == "global-accounts" && x.Kind == "identity-metadata" && x.Disposition == (plan ? "exclude" : "excluded"))) Fail();
        return values!.Select(x => x with { }).ToArray();
    }
    [System.Diagnostics.CodeAnalysis.DoesNotReturn] private static void Fail() => throw new ManagementOperationUnavailableException();
}
