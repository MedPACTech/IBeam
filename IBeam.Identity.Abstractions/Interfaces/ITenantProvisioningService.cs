namespace IBeam.Identity.Abstractions.Interfaces;

/// <summary>
/// Optional capability: creates an initial tenant and membership for a newly created user.
/// Providers/apps can implement this however they want.
/// </summary>
public interface ITenantProvisioningService
{
    /// <summary>
    /// Creates a tenant for a new user and makes that tenant the user's default/current tenant.
    /// Returns the created tenantId.
    /// </summary>
    Task<Guid> CreateTenantForNewUserAsync(
        Guid userId,
        string? email,
        CancellationToken ct = default);
}
