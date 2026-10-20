namespace IBeam.Management.CustomerIdentity;

public static class ManagementTenantSources
{
    public const string Unknown = "unknown";
    public const string SupportProvisioned = "support-provisioned";
    public const string SelfService = "self-service";
    public static bool IsValid(string? value) => value is Unknown or SupportProvisioned or SelfService;
}
/// <summary>Trusted target integration only. Invoke only with durable evidence of actual self-service creation.
/// No HTTP endpoint, customer request field or inferred legacy classification is supplied.</summary>
public interface IManagementTenantProvenanceRecorder
{
    Task RecordSelfServiceAsync(Guid tenantId, Guid creationEventId, CancellationToken ct = default);
}
