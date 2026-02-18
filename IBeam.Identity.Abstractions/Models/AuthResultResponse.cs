using IBeam.Identity.Abstractions.Models;

public sealed record AuthResultResponse(
    TokenResult? Token,
    bool RequiresTenantSelection,
    string? PreTenantToken,
    IReadOnlyList<TenantInfo> Tenants
)
{
    public static AuthResultResponse WithToken(TokenResult token)
        => new(token, false, null, Array.Empty<TenantInfo>());

    public static AuthResultResponse RequiresSelection(string preTenantToken, IReadOnlyList<TenantInfo> tenants)
        => new(null, true, preTenantToken, tenants);
}
