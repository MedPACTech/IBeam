namespace IBeam.Identity.Repositories.EntityFramework.OAuth.Entities;

public sealed class OAuthClientEntity
{
    public string ClientId { get; set; } = default!;
    public Guid? TenantId { get; set; }
    public string DisplayName { get; set; } = default!;
    public string ClientType { get; set; } = default!;
    public string RedirectUrisJson { get; set; } = "[]";
    public string AllowedGrantTypesJson { get; set; } = "[]";
    public string AllowedScopesJson { get; set; } = "[]";
    public string AllowedResourcesJson { get; set; } = "[]";
    public bool RequirePkce { get; set; }
    public string Status { get; set; } = default!;
    public string? ClientSecretHash { get; set; }
    public string? ClientSecretHashAlgorithm { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? UpdatedUtc { get; set; }
    public DateTimeOffset? SecretRotatedUtc { get; set; }
    public DateTimeOffset? ClientSecretExpiresUtc { get; set; }
    public DateTimeOffset? DisabledUtc { get; set; }
    public DateTimeOffset? RevokedUtc { get; set; }
}
