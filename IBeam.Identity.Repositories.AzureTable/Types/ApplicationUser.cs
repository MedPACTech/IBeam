using ElCamino.AspNetCore.Identity.AzureTable.Model;

namespace IBeam.Identity.Repositories.AzureTable.Types;

// ElCamino's model types are required for the AzureTable stores.
public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
}
