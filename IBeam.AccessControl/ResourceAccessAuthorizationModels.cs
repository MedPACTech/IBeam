namespace IBeam.AccessControl;

public sealed record ResourceAccessAuthorizationResult(
    bool Allowed,
    string? Reason,
    Guid? GrantId = null,
    string? AccessLevel = null)
{
    public static ResourceAccessAuthorizationResult Allow(Guid grantId, string accessLevel)
        => new(true, null, grantId, accessLevel);

    public static ResourceAccessAuthorizationResult Deny(string reason)
        => new(false, reason);
}
