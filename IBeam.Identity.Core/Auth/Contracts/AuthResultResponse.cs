namespace IBeam.Identity.Core.Auth.Contracts;

public sealed record TenantInfo(
    Guid TenantId,
    string? DisplayName,
    IReadOnlyList<string> Roles,
    string Status
);

public sealed record AuthResultResponse(
    AuthTokenResponse? Token,
    bool RequiresTenantSelection,
    string? PreTenantToken,
    IReadOnlyList<TenantInfo> Tenants
)
{
    public static AuthResultResponse WithToken(AuthTokenResponse token)
        => new(token, false, null, Array.Empty<TenantInfo>());

    public static AuthResultResponse RequiresSelection(string preTenantToken, IReadOnlyList<TenantInfo> tenants)
        => new(null, true, preTenantToken, tenants);
}
