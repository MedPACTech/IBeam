using System.Security.Claims;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;

namespace IBeam.Identity.Services.Authorization;

public sealed class OAuthEffectivePermissionResolver : IOAuthEffectivePermissionResolver
{
    private static readonly char[] ClaimValueSeparators = [',', ' '];

    private readonly IApiCredentialScopeCatalogProvider _scopeCatalog;
    private readonly IIBeamAccessControlService _accessControl;

    public OAuthEffectivePermissionResolver(
        IApiCredentialScopeCatalogProvider scopeCatalog,
        IIBeamAccessControlService accessControl)
    {
        _scopeCatalog = scopeCatalog;
        _accessControl = accessControl;
    }

    public async Task<OAuthEffectivePermissionResult> ResolveAsync(
        OAuthPermissionResolutionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Client);
        ArgumentNullException.ThrowIfNull(request.Subject);

        var requested = Normalize(request.RequestedScopes);
        if (requested.Count == 0)
            return new OAuthEffectivePermissionResult([], [], []);

        if (request.TenantId == Guid.Empty)
            return Empty(requested, OAuthScopeDenialReasons.TenantMismatch);

        if (!TenantMatches(request))
            return Empty(requested, OAuthScopeDenialReasons.TenantMismatch);

        if (!request.Client.IsActive || !AllowsValue(request.Client.AllowedResources, request.Resource, false))
            return Empty(requested, OAuthScopeDenialReasons.ResourceNotAllowed);

        if (request.Consent is null || !request.Consent.IsActive || !ConsentMatches(request))
            return Empty(requested, OAuthScopeDenialReasons.ConsentRequired);

        var apiCatalog = await _scopeCatalog.GetScopesAsync(request.TenantId, ct).ConfigureAwait(false);
        var accessCatalog = await _accessControl.GetAccessCatalogAsync(request.TenantId, ct).ConfigureAwait(false);
        var granted = new List<string>();
        var denied = new List<OAuthScopeDenial>();

        foreach (var scope in requested)
        {
            ct.ThrowIfCancellationRequested();
            var descriptor = Describe(scope, apiCatalog, accessCatalog, request.Subject);
            if (descriptor is null)
            {
                denied.Add(new(scope, OAuthScopeDenialReasons.UnknownScope));
                continue;
            }

            if (!AllowsValue(request.Client.AllowedScopes, scope, descriptor.WildcardCapable))
            {
                denied.Add(new(scope, OAuthScopeDenialReasons.ClientNotAllowed));
                continue;
            }

            if (!AllowsByPolicy(request.TenantPolicy, scope, descriptor.WildcardCapable))
            {
                denied.Add(new(scope, OAuthScopeDenialReasons.TenantPolicyDenied));
                continue;
            }

            if (!AllowsValue(request.Consent.Scopes, scope, descriptor.WildcardCapable))
            {
                denied.Add(new(scope, OAuthScopeDenialReasons.ConsentRequired));
                continue;
            }

            if (!await SubjectAllowsAsync(request.Subject, descriptor, ct).ConfigureAwait(false))
            {
                denied.Add(new(scope, OAuthScopeDenialReasons.SubjectNotAllowed));
                continue;
            }

            granted.Add(scope);
        }

        return new OAuthEffectivePermissionResult(granted, denied, BuildClaims(request, granted));
    }

    private async Task<bool> SubjectAllowsAsync(ClaimsPrincipal subject, ScopeDescriptor descriptor, CancellationToken ct)
    {
        if (HasClaimValue(subject, "role", descriptor.Canonical) ||
            HasClaimValue(subject, ClaimTypes.Role, descriptor.Canonical))
        {
            return true;
        }

        // A person delegating access through the authorization-code flow carries identity
        // roles, never the "scope", "scp" or "tool" claims an API credential is issued
        // with. Without this, no human subject can consent to an api-scope or tool scope
        // at all — including an Owner with unrestricted app access — and authorization
        // fails with invalid_scope after consent has already been approved.
        //
        // Delegation stays bounded by what the subject can actually reach: api-scope and
        // module name the same underlying thing, so both resolve through module access,
        // which already honours unrestricted roles. Tools have no per-user grant to check,
        // so they require unrestricted access.
        if (!HasMachineScopeClaims(subject))
        {
            switch (descriptor.Prefix)
            {
                case "api-scope":
                    if (await _accessControl.HasModuleAccessAsync(subject, descriptor.Key, AccessLevels.View, ct).ConfigureAwait(false))
                        return true;
                    break;
                case "tool":
                    if (await HasUnrestrictedAccessAsync(subject, ct).ConfigureAwait(false))
                        return true;
                    break;
            }
        }

        if (descriptor.WildcardCapable &&
            (HasClaimValue(subject, "role", $"{descriptor.Prefix}:*") ||
             HasClaimValue(subject, ClaimTypes.Role, $"{descriptor.Prefix}:*")))
        {
            return true;
        }

        return descriptor.Prefix switch
        {
            "api-scope" => HasClaimValue(subject, "scope", descriptor.Key) || HasClaimValue(subject, "scp", descriptor.Key),
            "tool" => HasClaimValue(subject, "tool", descriptor.Key),
            "agent" or "api-agent" => HasClaimValue(subject, "allowed_agent_key", descriptor.Key) || HasClaimValue(subject, "agent_key", descriptor.Key),
            "role" => await _accessControl.HasRoleAsync(subject, descriptor.Key, ct).ConfigureAwait(false),
            "permission" => await _accessControl.HasPermissionAsync(subject, descriptor.Key, ct).ConfigureAwait(false),
            "module" => await _accessControl.HasModuleAccessAsync(subject, descriptor.Key, AccessLevels.View, ct).ConfigureAwait(false),
            _ => false
        };
    }

    /// <summary>
    /// True when the subject was issued as a machine credential, which carries scope and
    /// tool claims directly. Those subjects keep the original, stricter evaluation.
    /// </summary>
    private static bool HasMachineScopeClaims(ClaimsPrincipal subject) =>
        subject.FindFirst("scope") is not null ||
        subject.FindFirst("scp") is not null ||
        subject.FindFirst("tool") is not null;

    private async Task<bool> HasUnrestrictedAccessAsync(ClaimsPrincipal subject, CancellationToken ct)
    {
        foreach (var role in UnrestrictedRoles)
            if (await _accessControl.HasRoleAsync(subject, role, ct).ConfigureAwait(false))
                return true;
        return false;
    }

    private static readonly string[] UnrestrictedRoles = ["Owner", "Administrator"];

    private static ScopeDescriptor? Describe(
        string scope,
        IReadOnlyList<ApiScopeCatalogItem> apiCatalog,
        AccessCatalogDto accessCatalog,
        ClaimsPrincipal subject)
    {
        var separator = scope.IndexOf(':');
        if (separator <= 0 || separator == scope.Length - 1)
            return null;

        var prefix = scope[..separator].ToLowerInvariant();
        var key = scope[(separator + 1)..];
        var apiItem = apiCatalog.FirstOrDefault(x =>
            string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase) &&
            CategoryMatches(prefix, x.Category));

        var known = prefix switch
        {
            "api-scope" or "tool" or "agent" or "api-agent" => apiItem is not null || CatalogContains(accessCatalog, prefix, key),
            "module" or "permission" => CatalogContains(accessCatalog, prefix, key),
            "role" => HasClaimValue(subject, "role", key) || HasClaimValue(subject, ClaimTypes.Role, key),
            _ => false
        };

        return known ? new ScopeDescriptor(scope, prefix, key, apiItem?.IsWildcardCapable == true) : null;
    }

    private static bool CatalogContains(AccessCatalogDto catalog, string prefix, string key)
    {
        var items = prefix switch
        {
            "api-scope" => catalog.ApiScopes,
            "tool" => catalog.Tools,
            "agent" or "api-agent" => catalog.Agents,
            "module" => catalog.Modules,
            "permission" => catalog.Permissions,
            _ => []
        };
        return items.Any(x => x.IsEnabled && string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    private static bool CategoryMatches(string prefix, string category) =>
        prefix switch
        {
            "api-scope" or "module" => string.Equals(category, "module", StringComparison.OrdinalIgnoreCase),
            "api-agent" => string.Equals(category, "agent", StringComparison.OrdinalIgnoreCase),
            _ => string.Equals(category, prefix, StringComparison.OrdinalIgnoreCase)
        };

    private static bool TenantMatches(OAuthPermissionResolutionRequest request)
    {
        var subjectTenant = Values(request.Subject, "tid", "tenant_id")
            .Select(value => Guid.TryParse(value, out var parsed) ? parsed : Guid.Empty)
            .FirstOrDefault(value => value != Guid.Empty);
        return subjectTenant == request.TenantId &&
               (request.Client.TenantId is null || request.Client.TenantId == request.TenantId);
    }

    private static bool ConsentMatches(OAuthPermissionResolutionRequest request) =>
        request.Consent is not null &&
        request.Consent.TenantId == request.TenantId &&
        request.Consent.UserId == ResolveSubjectId(request.Subject) &&
        string.Equals(request.Consent.ClientId, request.Client.ClientId, StringComparison.Ordinal) &&
        string.Equals(request.Consent.Resource, request.Resource, StringComparison.Ordinal);

    private static Guid ResolveSubjectId(ClaimsPrincipal subject) =>
        Values(subject, "sub", "uid", ClaimTypes.NameIdentifier)
            .Select(value => Guid.TryParse(value, out var parsed) ? parsed : Guid.Empty)
            .FirstOrDefault(value => value != Guid.Empty);

    private static bool AllowsByPolicy(OAuthTenantScopePolicy? policy, string scope, bool wildcardCapable)
    {
        if (policy is null)
            return true;
        if (AllowsValue(policy.DeniedScopes ?? [], scope, wildcardCapable))
            return false;
        return policy.AllowedScopes is null || AllowsValue(policy.AllowedScopes, scope, wildcardCapable);
    }

    private static bool AllowsValue(IEnumerable<string> allowed, string requested, bool wildcardCapable) =>
        allowed.Any(value =>
            string.Equals(value, requested, StringComparison.OrdinalIgnoreCase) ||
            (wildcardCapable && IsCategoryWildcard(value, requested)));

    private static bool IsCategoryWildcard(string candidate, string requested)
    {
        var separator = requested.IndexOf(':');
        return separator > 0 &&
               string.Equals(candidate, $"{requested[..separator]}:*", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasClaimValue(ClaimsPrincipal principal, string type, string value) =>
        Values(principal, type).Contains(value, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> Values(ClaimsPrincipal principal, params string[] types) =>
        types.SelectMany(principal.FindAll)
            .SelectMany(claim => claim.Value.Split(ClaimValueSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static IReadOnlyList<string> Normalize(IEnumerable<string>? scopes) =>
        (scopes ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static OAuthEffectivePermissionResult Empty(IReadOnlyList<string> requested, string reason) =>
        new([], requested.Select(scope => new OAuthScopeDenial(scope, reason)).ToList(), []);

    private static IReadOnlyList<ClaimItem> BuildClaims(OAuthPermissionResolutionRequest request, IReadOnlyList<string> granted)
    {
        var tenantId = request.TenantId.ToString("D");
        var claims = new List<ClaimItem>
        {
            new("tid", tenantId),
            new("tenant_id", tenantId),
            new("client_id", request.Client.ClientId),
            new("resource", request.Resource)
        };

        foreach (var scope in granted)
        {
            claims.Add(new("role", scope));
            var separator = scope.IndexOf(':');
            var prefix = scope[..separator].ToLowerInvariant();
            var key = scope[(separator + 1)..];
            claims.Add(prefix switch
            {
                "api-scope" => new ClaimItem("scope", key),
                "tool" => new ClaimItem("tool", key),
                "permission" => new ClaimItem("permission", key),
                "module" => new ClaimItem("module", key),
                "agent" or "api-agent" => new ClaimItem("allowed_agent_key", key),
                "role" => new ClaimItem(ClaimTypes.Role, key),
                _ => new ClaimItem("scope", scope)
            });
        }

        return claims
            .DistinctBy(x => $"{x.Type}\u001f{x.Value}", StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed record ScopeDescriptor(string Canonical, string Prefix, string Key, bool WildcardCapable);
}
