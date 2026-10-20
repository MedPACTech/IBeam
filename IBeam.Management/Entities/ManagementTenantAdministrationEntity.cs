namespace IBeam.Management.Entities;

public sealed record ManagementTenantAdministrationEntity
{
    public Guid IdentityRealmId { get; init; }
    public string ApplicationKey { get; init; } = "";
    public string Environment { get; init; } = "";
    public Guid TenantId { get; init; }
    public string Source { get; init; } = "unknown";
    public string? ExternalReference { get; init; }
    public Guid? CreateOperationId { get; init; }
    public Guid LastOperationId { get; init; }
    public string Phase { get; init; } = "creating";
    public string Version { get; init; } = "";
    public override string ToString() => "Tenant administration metadata (external reference redacted)";
}
