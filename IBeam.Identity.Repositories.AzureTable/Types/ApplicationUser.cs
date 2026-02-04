using ElCamino.AspNetCore.Identity.AzureTable.Model;

namespace IBeam.Identity.Repositories.AzureTable.Types;

// ElCamino's model types are required for the AzureTable stores.
public class ApplicationUser : IdentityUser
{
    // optional tenant fields (safe to add; table storage is schema-less)
    public Guid? TenantId { get; set; }
    public string DisplayName { get; set; } = "";
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}
