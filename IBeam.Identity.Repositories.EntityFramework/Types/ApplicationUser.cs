
namespace IBeam.Identity.Repositories.EntityFramework.Types;

public class ApplicationUser : Microsoft.AspNetCore.Identity.IdentityUser<Guid>
{
    public Guid? TenantId { get; set; }
    public string DisplayName { get; set; } = "";
    public string? ProfileExtensionsJson { get; set; }
    public DateTimeOffset? ProfileExtensionsUpdatedAtUtc { get; set; }
}
