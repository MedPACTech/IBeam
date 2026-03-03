namespace IBeam.Repositories.Abstractions;


//TODO: move this to Core keep abstraction project only for interfaces
public class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }

    public TenantContext() { }
    public TenantContext(Guid tenantId) => TenantId = tenantId;

    public void SetTenantId(Guid tenantId) => TenantId = tenantId;
    public void ClearTenantId() => TenantId = null;

    public bool IsTenantIdSet() => TenantId.HasValue && TenantId.Value != Guid.Empty;
}