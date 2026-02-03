
namespace IBeam.Identity.Storage.EntityFramework.Types;

public class ApplicationUser : Microsoft.AspNetCore.Identity.IdentityUser<Guid>
{
    public Guid? TenantId { get; set; }
    public string DisplayName { get; set; } = "";
}