

namespace IBeam.Identity.Repositories.EntityFramework.Tenants.Entities;

// UserTenants table entity: PK = "USR#{userId}", RK = "TEN#{tenantId}"
public class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? NormalizedName { get; set; }
    public string Status { get; set; } = "Active";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
