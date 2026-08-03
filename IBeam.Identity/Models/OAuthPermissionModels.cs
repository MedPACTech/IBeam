using System.Security.Claims;

namespace IBeam.Identity.Models;

public static class OAuthScopeDenialReasons
{
    public const string UnknownScope = "unknown_scope";
    public const string ClientNotAllowed = "client_not_allowed";
    public const string SubjectNotAllowed = "subject_not_allowed";
    public const string TenantPolicyDenied = "tenant_policy_denied";
    public const string ConsentRequired = "consent_required";
    public const string TenantMismatch = "tenant_mismatch";
    public const string ResourceNotAllowed = "resource_not_allowed";
}

public sealed record OAuthTenantScopePolicy(
    IReadOnlyList<string>? AllowedScopes = null,
    IReadOnlyList<string>? DeniedScopes = null);

public sealed record OAuthPermissionResolutionRequest(
    Guid TenantId,
    OAuthClientRecord Client,
    OAuthConsentRecord? Consent,
    ClaimsPrincipal Subject,
    IReadOnlyList<string> RequestedScopes,
    string Resource,
    OAuthTenantScopePolicy? TenantPolicy = null);

public sealed record OAuthScopeDenial(string Scope, string Reason);

public sealed record OAuthEffectivePermissionResult(
    IReadOnlyList<string> GrantedScopes,
    IReadOnlyList<OAuthScopeDenial> DeniedScopes,
    IReadOnlyList<ClaimItem> Claims)
{
    public bool IsGranted(string scope) =>
        GrantedScopes.Contains(scope, StringComparer.OrdinalIgnoreCase);
}
