using System.Security.Cryptography;
using System.Text.Json;
using IBeam.Management.Operations;
using IBeam.Management.TenantData;

namespace IBeam.Management.TenantData.Target.Services;

public static class ManagementTenantPurgeProtocol
{
    public static ManagementTenantPurgeDefinition Definition(ManagementTenantPurgeDefinition value)
    {
        if (value is null || value.ContractVersion != 1 || !ManagementTenantExportMetadata.Key(value.HandlerKey) || value.InventoryVersion < 1 ||
            value.Categories is not { Count: > 1 and <= 32 }) Unavailable();
        var items = value!.Categories.ToArray();
        if (items.Any(x => x is null || !ManagementTenantExportMetadata.Key(x.Key) || x.Kind is not ("dataset" or "attachment" or "identity-metadata" or "external" or "backup") ||
                x.Disposition is not ("delete" or "retain" or "exclude")) || items.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != items.Length ||
            !items.Any(x => x.Disposition == "delete") || !items.Any(x => x.Key == "global-accounts" && x.Kind == "identity-metadata" && x.Disposition == "exclude")) Unavailable();
        return value with { Categories = Array.AsReadOnly(items.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x with { }).ToArray()) };
    }
    public static string Fingerprint(ManagementTenantPurgeDefinition definition) => Hash(Definition(definition));
    public static string Binding(ManagementTenantPurgeExecution e, ManagementTenantPurgePlan plan) => Hash(new {
        e.ApplicationKey, e.Environment, e.Authority.OrganizationId, e.Authority.ApplicationId, e.Authority.RealmId,
        e.Authority.TenantId, e.Authority.ActorUserId, plan.PlanId, plan.PolicyKey, plan.ExpectedVersion, plan.InventoryFingerprint, plan.CreatedUtc, plan.ExpiresUtc });
    public static ManagementTenantPurgePlan Plan(ManagementTenantPurgePlan plan, ManagementTenantPurgeExecution e,
        ManagementTenantPurgeDefinition definition, DateTimeOffset now)
    {
        var frozen = Definition(definition);
        if (plan is null || plan.ContractVersion != 1 || plan.TenantId != e.Authority.TenantId || plan.PlanId == Guid.Empty ||
            plan.PolicyKey != e.PolicyKey || plan.ExpectedVersion is not { Length: > 0 and <= 256 } || plan.ExpectedVersion.Trim() != plan.ExpectedVersion ||
            plan.ExpectedVersion == "*" || plan.ExpectedVersion.Any(char.IsControl) || plan.InventoryFingerprint != Fingerprint(frozen) ||
            plan.InventoryFingerprint != e.InventoryFingerprint || plan.CreatedUtc.Offset != TimeSpan.Zero || plan.ExpiresUtc.Offset != TimeSpan.Zero ||
            plan.CreatedUtc > now || plan.ExpiresUtc <= now || plan.ExpiresUtc <= plan.CreatedUtc || plan.ExpiresUtc - plan.CreatedUtc > TimeSpan.FromMinutes(5) ||
            plan.ConfirmationBinding != Binding(e, plan) || plan.Categories is null || !plan.Categories.SequenceEqual(frozen.Categories))
            throw new ManagementOperationPreconditionException();
        return plan with { Categories = plan.Categories.Select(x => x with { }).ToArray() };
    }
    public static ManagementTenantPurgeResult Result(ManagementTenantPurgeResult result, ManagementTenantPurgeExecution e, ManagementTenantPurgeDefinition definition)
    {
        if (result is null || result.TenantId != e.Authority.TenantId || result.OperationId != e.JobId || result.Plan is not null ||
            result.Outcome is not ("accepted" or "found" or "unknown") || (result.Outcome == "unknown") != (result.Job is null)) Unavailable();
        if (result!.Job is not { } job) return new(e.Authority.TenantId, e.JobId, "unknown", null, null);
        var inventory = Definition(definition);
        if (job.ContractVersion != 1 || job.TenantId != e.Authority.TenantId || job.JobId != e.JobId || job.PlanId == Guid.Empty ||
            job.State is not ("requested" or "fencing" or "purging" or "verifying" or "partially-completed" or "succeeded" or "failed" or "cancelled" or "unknown") ||
            job.CreatedUtc.Offset != TimeSpan.Zero || job.UpdatedUtc.Offset != TimeSpan.Zero || job.UpdatedUtc < job.CreatedUtc ||
            job.FailureCode is not (null or "fence-failed" or "handler-failed" or "verification-failed" or "storage-unavailable" or "cancelled" or "unknown") ||
            job.Coverage is null || job.Coverage.Length != inventory.Categories.Count) Unavailable();
        var coverage = job.Coverage!.ToArray();
        if (coverage.Any(x => x is null || !inventory.Categories.Any(y => y.Key == x.Key && y.Kind == x.Kind) || x.Disposition is not ("pending" or "deleted" or "retained" or "excluded")) ||
            coverage.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != coverage.Length ||
            coverage.Any(x => inventory.Categories.Single(y => y.Key == x.Key).Disposition switch {
                "exclude" => x.Disposition != "excluded", "retain" => x.Disposition != "retained", _ => x.Disposition == "excluded" }) ||
            job.State == "succeeded" && (job.FailureCode is not null || coverage.Any(x => inventory.Categories.Single(y => y.Key == x.Key).Disposition == "delete" && x.Disposition != "deleted")) ||
            job.State == "partially-completed" && (coverage.Any(x => x.Disposition == "pending") || !coverage.Any(x => x.Disposition == "retained" && inventory.Categories.Single(y => y.Key == x.Key).Disposition == "delete"))) Unavailable();
        return result with { Job = job with { Coverage = coverage.Select(x => x with { }).ToArray() } };
    }
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    [System.Diagnostics.CodeAnalysis.DoesNotReturn] private static void Unavailable() => throw new ManagementOperationUnavailableException();
}
