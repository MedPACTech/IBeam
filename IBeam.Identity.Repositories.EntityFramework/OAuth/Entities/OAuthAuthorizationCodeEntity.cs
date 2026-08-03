namespace IBeam.Identity.Repositories.EntityFramework.OAuth.Entities;

public sealed class OAuthAuthorizationCodeEntity
{
    public string CodeHash { get; set; } = default!;
    public string ClientId { get; set; } = default!;
    public string RedirectUri { get; set; } = default!;
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string ScopesJson { get; set; } = "[]";
    public string Resource { get; set; } = default!;
    public string CodeChallenge { get; set; } = default!;
    public string CodeChallengeMethod { get; set; } = default!;
    public long CreatedUtcTicks { get; set; }
    public long ExpiresUtcTicks { get; set; }
    public long? ConsumedUtcTicks { get; set; }
}
