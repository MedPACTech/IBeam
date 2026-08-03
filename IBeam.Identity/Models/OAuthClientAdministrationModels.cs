namespace IBeam.Identity.Models;

public sealed class CreateOAuthClientRequest
{
    public string? ClientId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ClientType { get; set; } = OAuthClientTypes.Public;
    public List<string> RedirectUris { get; set; } = [];
    public List<string> AllowedGrantTypes { get; set; } = [OAuthGrantTypes.AuthorizationCode];
    public List<string> AllowedScopes { get; set; } = [];
    public List<string> AllowedResources { get; set; } = [];
    public bool RequirePkce { get; set; } = true;
    public DateTimeOffset? ClientSecretExpiresUtc { get; set; }
}

public sealed class UpdateOAuthClientRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public List<string> RedirectUris { get; set; } = [];
    public List<string> AllowedGrantTypes { get; set; } = [];
    public List<string> AllowedScopes { get; set; } = [];
    public List<string> AllowedResources { get; set; } = [];
    public bool RequirePkce { get; set; } = true;
    public DateTimeOffset? ClientSecretExpiresUtc { get; set; }
}

public sealed record OAuthClientInfo(
    string ClientId,
    Guid TenantId,
    string DisplayName,
    string ClientType,
    IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> AllowedGrantTypes,
    IReadOnlyList<string> AllowedScopes,
    IReadOnlyList<string> AllowedResources,
    bool RequirePkce,
    string Status,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? UpdatedUtc,
    DateTimeOffset? SecretRotatedUtc,
    DateTimeOffset? ClientSecretExpiresUtc,
    DateTimeOffset? DisabledUtc,
    DateTimeOffset? RevokedUtc)
{
    public static OAuthClientInfo FromRecord(OAuthClientRecord record) => new(
        record.ClientId,
        record.TenantId ?? Guid.Empty,
        record.DisplayName,
        record.ClientType,
        record.RedirectUris,
        record.AllowedGrantTypes,
        record.AllowedScopes,
        record.AllowedResources,
        record.RequirePkce,
        record.Status,
        record.CreatedUtc,
        record.UpdatedUtc,
        record.SecretRotatedUtc,
        record.ClientSecretExpiresUtc,
        record.DisabledUtc,
        record.RevokedUtc);
}

public sealed record OAuthClientCreatedResult(OAuthClientInfo Client, string? ClientSecret);

public sealed record OAuthClientSecretRotatedResult(OAuthClientInfo Client, string ClientSecret);
