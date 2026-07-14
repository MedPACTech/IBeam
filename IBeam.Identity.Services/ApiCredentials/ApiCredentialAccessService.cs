using System.Security.Claims;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Services.Authorization;

namespace IBeam.Identity.Services.ApiCredentials;

public sealed class ApiCredentialAccessService : IApiCredentialAccessService
{
    private readonly IApiCredentialStore _credentials;
    private readonly IIBeamAccessGrantStore _grants;
    private readonly IPermissionCatalogProvider _permissionCatalog;
    private readonly IPermissionGrantResolver _permissionGrantResolver;
    private readonly IEnumerable<IApiCredentialAccessRuleProvider> _ruleProviders;

    public ApiCredentialAccessService(
        IApiCredentialStore credentials,
        IIBeamAccessGrantStore grants,
        IPermissionCatalogProvider permissionCatalog,
        IPermissionGrantResolver permissionGrantResolver,
        IEnumerable<IApiCredentialAccessRuleProvider> ruleProviders)
    {
        _credentials = credentials;
        _grants = grants;
        _permissionCatalog = permissionCatalog;
        _permissionGrantResolver = permissionGrantResolver;
        _ruleProviders = ruleProviders;
    }

    public async Task<ApiCredentialContext?> GetCurrentApiCredentialAsync(
        ClaimsPrincipal principal,
        CancellationToken ct = default)
    {
        if (!IsApiCredentialPrincipal(principal))
            return null;

        if (!TryResolveTenantAndCredential(principal, out var tenantId, out var credentialId))
            return null;

        var credential = await _credentials.GetAsync(tenantId, credentialId, ct).ConfigureAwait(false);
        if (credential is null)
            return null;

        return new ApiCredentialContext(
            credential.TenantId,
            credential.CredentialId,
            credential.DisplayName,
            credential.AgentKey,
            credential.IsActive(DateTimeOffset.UtcNow),
            credential.RoleNames,
            credential.RoleIds);
    }

    public async Task<ApiCredentialAccessContextDto> GetCurrentAccessContextAsync(
        ClaimsPrincipal principal,
        string? requestedAgentKey = null,
        CancellationToken ct = default)
    {
        if (!IsApiCredentialPrincipal(principal))
            throw new IdentityUnauthorizedException("API credential principal is required.");

        if (!TryResolveTenantAndCredential(principal, out var tenantId, out var credentialId))
            throw new IdentityValidationException("API credential claims must include tenant and credential identifiers.");

        var credential = await _credentials.GetAsync(tenantId, credentialId, ct).ConfigureAwait(false)
            ?? throw new IdentityNotFoundException($"API credential '{credentialId}' was not found.");

        return await BuildAccessContextAsync(ApiCredentialInfo.FromRecord(credential), requestedAgentKey, ct)
            .ConfigureAwait(false);
    }

    public async Task<ApiCredentialAccessContextDto> BuildAccessContextAsync(
        ApiCredentialInfo credential,
        string? requestedAgentKey = null,
        CancellationToken ct = default)
    {
        var normalized = NormalizeRoleStrings(credential);
        var grants = await _grants
            .GetGrantsAsync(
                credential.TenantId,
                AccessSubjectTypes.ApiCredential,
                credential.Id.ToString("D"),
                ct)
            .ConfigureAwait(false);

        var resources = BuildResourceAccess(normalized.Resources, grants);
        var permissions = await ResolvePermissionNamesAsync(credential, ct).ConfigureAwait(false);
        var canActAsRequestedAgent = string.IsNullOrWhiteSpace(requestedAgentKey) ||
            AgentMatches(normalized.AllowedAgentKeys, requestedAgentKey!);

        var context = new ApiCredentialAccessContextDto(
            PrincipalType: AccessSubjectTypes.ApiCredential,
            TenantId: credential.TenantId,
            CredentialId: credential.Id,
            CredentialName: credential.DisplayName,
            AgentKey: credential.AgentKey,
            AgentDisplayName: credential.AgentDisplayName,
            IsActive: credential.IsActive,
            Roles: credential.RoleNames,
            RoleIds: credential.RoleIds,
            Permissions: permissions,
            ApiScopes: normalized.ApiScopes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            Tools: normalized.Tools.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            AllowedAgentKeys: normalized.AllowedAgentKeys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            Resources: resources,
            Capabilities: new ApiCredentialAccessCapabilitiesDto(
                CanUseMcp: ContainsWildcardOrValue(normalized.Tools, "mcp"),
                CanAccessWorkApi: ContainsWildcardOrValue(normalized.ApiScopes, "work"),
                CanActAsRequestedAgent: canActAsRequestedAgent));

        foreach (var provider in _ruleProviders)
        {
            await provider.EvaluateAsync(
                new ApiCredentialAccessEvaluationContext(
                    credential.TenantId,
                    credential,
                    context,
                    requestedAgentKey,
                    null,
                    null,
                    null),
                ct).ConfigureAwait(false);
        }

        return context;
    }

    public async Task<bool> HasApiScopeAsync(ClaimsPrincipal principal, string moduleKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(moduleKey))
            throw new IdentityValidationException("moduleKey is required.");

        var context = await GetCurrentAccessContextAsync(principal, null, ct).ConfigureAwait(false);
        return ContainsWildcardOrValue(context.ApiScopes, moduleKey);
    }

    public async Task<bool> HasToolAccessAsync(ClaimsPrincipal principal, string toolKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(toolKey))
            throw new IdentityValidationException("toolKey is required.");

        var context = await GetCurrentAccessContextAsync(principal, null, ct).ConfigureAwait(false);
        return ContainsWildcardOrValue(context.Tools, toolKey);
    }

    public async Task<bool> CanActAsAgentAsync(ClaimsPrincipal principal, string agentKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(agentKey))
            throw new IdentityValidationException("agentKey is required.");

        var context = await GetCurrentAccessContextAsync(principal, agentKey, ct).ConfigureAwait(false);
        return context.Capabilities.CanActAsRequestedAgent;
    }

    public async Task<bool> CanCredentialActAsAgentAsync(
        Guid tenantId,
        Guid credentialId,
        string agentKey,
        CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            throw new IdentityValidationException("tenantId is required.");
        if (credentialId == Guid.Empty)
            throw new IdentityValidationException("credentialId is required.");
        if (string.IsNullOrWhiteSpace(agentKey))
            throw new IdentityValidationException("agentKey is required.");

        var credential = await _credentials.GetAsync(tenantId, credentialId, ct).ConfigureAwait(false)
            ?? throw new IdentityNotFoundException($"API credential '{credentialId}' was not found.");

        var context = await BuildAccessContextAsync(ApiCredentialInfo.FromRecord(credential), agentKey, ct)
            .ConfigureAwait(false);

        return context.Capabilities.CanActAsRequestedAgent;
    }

    public async Task<bool> HasResourceAccessAsync(
        ClaimsPrincipal principal,
        string resourceType,
        string resourceId,
        string minimumAccessLevel = AccessLevels.View,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(resourceType))
            throw new IdentityValidationException("resourceType is required.");
        if (string.IsNullOrWhiteSpace(resourceId))
            throw new IdentityValidationException("resourceId is required.");

        var context = await GetCurrentAccessContextAsync(principal, null, ct).ConfigureAwait(false);
        if (context.Resources.TryGetValue(resourceType.Trim(), out var entries) &&
            entries.Any(x =>
                (string.Equals(x.ResourceId, resourceId.Trim(), StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(x.ResourceId, "*", StringComparison.OrdinalIgnoreCase)) &&
                AccessRank(x.AccessLevel) >= AccessRank(minimumAccessLevel)))
            return true;

        foreach (var provider in _ruleProviders)
        {
            var decisions = await provider.EvaluateAsync(
                new ApiCredentialAccessEvaluationContext(
                    context.TenantId,
                    new ApiCredentialInfo(
                        context.CredentialId,
                        context.TenantId,
                        context.CredentialName,
                        context.AgentKey,
                        context.Roles,
                        context.RoleIds,
                        string.Empty,
                        DateTimeOffset.MinValue,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        false,
                        AgentDisplayName: context.AgentDisplayName,
                        AllowedAgentKeys: context.AllowedAgentKeys),
                    context,
                    null,
                    resourceType.Trim(),
                    resourceId.Trim(),
                    minimumAccessLevel),
                ct).ConfigureAwait(false);

            if (decisions.Any(x => x.IsAllowed))
                return true;
        }

        return false;
    }

    public async Task RequireApiScopeAsync(ClaimsPrincipal principal, string moduleKey, CancellationToken ct = default)
    {
        if (!await HasApiScopeAsync(principal, moduleKey, ct).ConfigureAwait(false))
            throw new IdentityUnauthorizedException($"API scope '{moduleKey}' is required.");
    }

    public async Task RequireToolAccessAsync(ClaimsPrincipal principal, string toolKey, CancellationToken ct = default)
    {
        if (!await HasToolAccessAsync(principal, toolKey, ct).ConfigureAwait(false))
            throw new IdentityUnauthorizedException($"Tool access '{toolKey}' is required.");
    }

    public async Task RequireAgentAccessAsync(ClaimsPrincipal principal, string agentKey, CancellationToken ct = default)
    {
        if (!await CanActAsAgentAsync(principal, agentKey, ct).ConfigureAwait(false))
            throw new IdentityUnauthorizedException($"Agent access '{agentKey}' is required.");
    }

    public async Task RequireResourceAccessAsync(
        ClaimsPrincipal principal,
        string resourceType,
        string resourceId,
        string minimumAccessLevel = AccessLevels.View,
        CancellationToken ct = default)
    {
        if (!await HasResourceAccessAsync(principal, resourceType, resourceId, minimumAccessLevel, ct).ConfigureAwait(false))
            throw new IdentityUnauthorizedException($"Resource access '{resourceType}:{resourceId}:{minimumAccessLevel}' is required.");
    }

    private async Task<IReadOnlyList<string>> ResolvePermissionNamesAsync(ApiCredentialInfo credential, CancellationToken ct)
    {
        var catalog = await _permissionCatalog.GetExposedPermissionsAsync(ct).ConfigureAwait(false);
        var roleNames = credential.RoleNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roleIds = credential.RoleIds.ToHashSet();
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var permission in catalog)
        {
            var names = string.IsNullOrWhiteSpace(permission.PermissionName)
                ? Array.Empty<string>()
                : new[] { permission.PermissionName.Trim() };
            var ids = permission.PermissionId.HasValue ? new[] { permission.PermissionId.Value } : Array.Empty<Guid>();

            var grants = await _permissionGrantResolver.ResolveAsync(credential.TenantId, names, ids, ct).ConfigureAwait(false);
            if (grants.RoleNames.Any(roleNames.Contains) || grants.RoleIds.Any(roleIds.Contains))
            {
                if (!string.IsNullOrWhiteSpace(permission.PermissionName))
                    result.Add(permission.PermissionName.Trim());
            }
        }

        return result.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static Dictionary<string, IReadOnlyList<ApiCredentialResourceAccessDto>> BuildResourceAccess(
        IReadOnlyDictionary<string, IReadOnlyList<ApiCredentialResourceAccessDto>> roleResources,
        IReadOnlyList<AccessGrant> grants)
    {
        var working = new Dictionary<string, List<ApiCredentialResourceAccessDto>>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in roleResources)
        {
            working[group.Key] = group.Value.ToList();
        }

        foreach (var grant in grants.Where(x => x.IsActive))
        {
            if (!working.TryGetValue(grant.ResourceType, out var list))
            {
                list = [];
                working[grant.ResourceType] = list;
            }

            list.Add(new ApiCredentialResourceAccessDto(
                grant.ResourceId,
                Slug: null,
                grant.AccessLevel,
                "grant"));
        }

        return working.ToDictionary(
            x => x.Key,
            x => (IReadOnlyList<ApiCredentialResourceAccessDto>)x.Value
                .GroupBy(v => v.ResourceId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(v => AccessRank(v.AccessLevel)).First())
                .OrderBy(v => v.ResourceId, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static NormalizedApiCredentialAccess NormalizeRoleStrings(ApiCredentialInfo credential)
    {
        var apiScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allowedAgents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resources = new Dictionary<string, List<ApiCredentialResourceAccessDto>>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(credential.AgentKey))
            allowedAgents.Add(credential.AgentKey.Trim());

        foreach (var explicitAgent in credential.AllowedAgentKeys ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(explicitAgent))
                allowedAgents.Add(explicitAgent.Trim());
        }

        foreach (var rawRole in credential.RoleNames.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()))
        {
            if (TryConsumePrefix(rawRole, "api-scope:", out var scope))
                AddNormalized(apiScopes, scope);
            else if (TryConsumePrefix(rawRole, "tool:", out var tool))
                AddNormalized(tools, tool);
            else if (TryConsumePrefix(rawRole, "api-agent:", out var apiAgent))
                AddNormalized(allowedAgents, apiAgent);
            else if (TryConsumePrefix(rawRole, "agent:", out var agent))
                AddNormalized(allowedAgents, agent);
            else if (TryConsumePrefix(rawRole, "product:", out var product))
                AddResource(resources, "product", product, AccessLevels.View, "role");
            else if (TryConsumePrefix(rawRole, "project:", out var project))
                AddResource(resources, "project", project, AccessLevels.View, "role");
        }

        return new NormalizedApiCredentialAccess(
            apiScopes,
            tools,
            allowedAgents,
            resources.ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<ApiCredentialResourceAccessDto>)x.Value,
                StringComparer.OrdinalIgnoreCase));
    }

    private static void AddNormalized(HashSet<string> values, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        values.Add(value.Trim());
    }

    private static void AddResource(
        Dictionary<string, List<ApiCredentialResourceAccessDto>> resources,
        string resourceType,
        string resourceIdOrSlug,
        string accessLevel,
        string source)
    {
        if (string.IsNullOrWhiteSpace(resourceIdOrSlug))
            return;

        if (!resources.TryGetValue(resourceType, out var list))
        {
            list = [];
            resources[resourceType] = list;
        }

        var value = resourceIdOrSlug.Trim();
        list.Add(new ApiCredentialResourceAccessDto(value, value == "*" ? null : value, accessLevel, source));
    }

    private static bool TryConsumePrefix(string value, string prefix, out string remainder)
    {
        remainder = string.Empty;
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        remainder = value[prefix.Length..].Trim();
        return remainder.Length > 0;
    }

    private static bool ContainsWildcardOrValue(IEnumerable<string> values, string value)
        => values.Any(x =>
            string.Equals(x, "*", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x, value.Trim(), StringComparison.OrdinalIgnoreCase));

    private static bool AgentMatches(IEnumerable<string> allowedAgentKeys, string requestedAgentKey)
        => ContainsWildcardOrValue(allowedAgentKeys, requestedAgentKey);

    private static int AccessRank(string? accessLevel)
        => accessLevel?.Trim().ToLowerInvariant() switch
        {
            AccessLevels.Manage => 20,
            AccessLevels.Edit => 10,
            AccessLevels.View => 0,
            "*" => 100,
            _ => 0
        };

    private static bool IsApiCredentialPrincipal(ClaimsPrincipal principal)
        => principal?.Identity?.IsAuthenticated == true &&
           (string.Equals(principal.FindFirstValue("api_subject_type"), "credential", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(principal.FindFirstValue("api_credential_id")));

    private static bool TryResolveTenantAndCredential(ClaimsPrincipal principal, out Guid tenantId, out Guid credentialId)
    {
        tenantId = Guid.Empty;
        credentialId = Guid.Empty;

        var tenantRaw = principal.FindFirstValue("tid") ?? principal.FindFirstValue("tenant_id");
        var credentialRaw = principal.FindFirstValue("api_credential_id");

        return Guid.TryParse(tenantRaw, out tenantId) &&
               Guid.TryParse(credentialRaw, out credentialId) &&
               tenantId != Guid.Empty &&
               credentialId != Guid.Empty;
    }

    private sealed record NormalizedApiCredentialAccess(
        HashSet<string> ApiScopes,
        HashSet<string> Tools,
        HashSet<string> AllowedAgentKeys,
        IReadOnlyDictionary<string, IReadOnlyList<ApiCredentialResourceAccessDto>> Resources);
}
