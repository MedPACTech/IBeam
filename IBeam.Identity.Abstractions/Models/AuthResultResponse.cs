using IBeam.Identity.Abstractions.Models;

public sealed record AuthResultResponse(
    TokenResult? Token,
    bool RequiresTenantSelection,
    string? PreTenantToken,
    IReadOnlyList<TenantInfo> Tenants,
    bool IsNewUser = false
)
{
    public static AuthResultResponse WithToken(TokenResult token, bool isNewUser = false)
        => new(token, false, null, Array.Empty<TenantInfo>(), isNewUser);

    public static AuthResultResponse RequiresSelection(string preTenantToken, IReadOnlyList<TenantInfo> tenants, bool isNewUser = false)
        => new(null, true, preTenantToken, tenants, isNewUser);
}
