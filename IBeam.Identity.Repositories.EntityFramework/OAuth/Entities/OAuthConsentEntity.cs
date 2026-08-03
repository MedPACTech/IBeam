namespace IBeam.Identity.Repositories.EntityFramework.OAuth.Entities;

public sealed class OAuthConsentEntity
{
    public Guid ConsentId { get; set; }
    public string LookupKey { get; set; } = default!;
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string ClientId { get; set; } = default!;
    public string Resource { get; set; } = default!;
    public string ScopesJson { get; set; } = "[]";
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public DateTimeOffset? RevokedUtc { get; set; }
}
