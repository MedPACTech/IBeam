

using IBeam.Identity.Repositories.EntityFramework.Types;

namespace IBeam.Identity.Repositories.EntityFramework.Tenants.Entities;

// UserTenants table entity: PK = "USR#{userId}", RK = "TEN#{tenantId}"
public class TenantUser
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }

    public string Status { get; set; } = "Active";
    public string RolesCsv { get; set; } = "";   // simple to start
    public bool IsDefault { get; set; }
    public DateTimeOffset? LastSelectedAt { get; set; }

    public Tenant Tenant { get; set; } = default!;
    public ApplicationUser User { get; set; } = default!;
}

