namespace IBeam.Identity.Abstractions.Options;

public sealed class OAuthOptions
{
    public const string SectionName = "IBeam:Identity:OAuth";

    public int StateTtlMinutes { get; init; } = 10;
    public Dictionary<string, OAuthProviderOptions> Providers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class OAuthProviderOptions
{
    public bool Enabled { get; init; } = false;
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string AuthorizationEndpoint { get; init; } = string.Empty;
    public string TokenEndpoint { get; init; } = string.Empty;
    public string UserInfoEndpoint { get; init; } = string.Empty;
    public string Scope { get; init; } = "openid profile email";
}
