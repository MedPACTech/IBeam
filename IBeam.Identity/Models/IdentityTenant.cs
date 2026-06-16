namespace IBeam.Identity.Models;

public sealed record IdentityTenant(
    Guid TenantId,
    string Name,
    string? NormalizedName = null,
    string Status = IdentityTenantStatuses.Active,
    DateTimeOffset? CreatedAt = null,
    DateTimeOffset? UpdatedAt = null)
{
    public bool IsActive
        => string.Equals(Status, IdentityTenantStatuses.Active, StringComparison.OrdinalIgnoreCase);

    public static IdentityTenant FromTenantInfo(TenantInfo tenant, DateTimeOffset? createdAt = null, DateTimeOffset? updatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        return new IdentityTenant(
            tenant.TenantId,
            tenant.Name,
            NormalizeName(tenant.Name),
            tenant.IsActive ? IdentityTenantStatuses.Active : IdentityTenantStatuses.Disabled,
            createdAt,
            updatedAt);
    }

    public static string NormalizeName(string? name)
        => (name ?? string.Empty).Trim().ToUpperInvariant();
}

public static class IdentityTenantStatuses
{
    public const string Active = "Active";
    public const string Disabled = "Disabled";
}
