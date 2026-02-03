

namespace IBeam.Identity.Storage.EntityFramework.Tenants.Entities;

// UserTenants table entity: PK = "USR#{userId}", RK = "TEN#{tenantId}"
public class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}
