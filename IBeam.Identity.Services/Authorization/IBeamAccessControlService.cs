using System.Security.Claims;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Services.Authorization;

public sealed class IBeamAccessControlService : IIBeamAccessControlService
{
    private static readonly string[] UserIdClaimTypes =
    [
        "sub",
        ClaimTypes.NameIdentifier,
        "uid",
        "user_id"
    ];

    private readonly IIBeamAccessGrantStore _grants;
    private readonly IPermissionCatalogProvider _catalog;
    private readonly IPermissionGrantResolver _permissionGrantResolver;
    private readonly IOptionsMonitor<IBeamAccessControlOptions> _options;
    private readonly IEnumerable<IIBeamAccessCatalogProvider> _catalogProviders;
    private readonly IEnumerable<IIBeamAccessRuleProvider> _ruleProviders;

    public IBeamAccessControlService(
        IIBeamAccessGrantStore grants,
        IPermissionCatalogProvider catalog,
        IPermissionGrantResolver permissionGrantResolver,
        IOptionsMonitor<IBeamAccessControlOptions> options,
        IEnumerable<IIBeamAccessCatalogProvider> catalogProviders,
        IEnumerable<IIBeamAccessRuleProvider> ruleProviders)
    {
        _grants = grants ?? throw new ArgumentNullException(nameof(grants));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _permissionGrantResolver = permissionGrantResolver ?? throw new ArgumentNullException(nameof(permissionGrantResolver));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _catalogProviders = catalogProviders ?? throw new ArgumentNullException(nameof(catalogProviders));
        _ruleProviders = ruleProviders ?? throw new ArgumentNullException(nameof(ruleProviders));
    }

    public Task<bool> HasRoleAsync(ClaimsPrincipal principal, string roleName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(roleName))
            throw new IdentityValidationException("roleName is required.");

        return Task.FromResult(GetRoleNames(principal).Contains(roleName.Trim()));
    }

    public async Task<bool> HasPermissionAsync(ClaimsPrincipal principal, string permissionName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(permissionName))
            throw new IdentityValidationException("permissionName is required.");

        var tenantId = ResolveTenantId(principal);
        if (tenantId == Guid.Empty || principal?.Identity?.IsAuthenticated != true)
            return false;

        if (HasUnrestrictedAccess(principal, ownerOnly: false))
            return true;

        var grants = await _permissionGrantResolver
            .ResolveAsync(tenantId, [permissionName.Trim()], Array.Empty<Guid>(), ct)
            .ConfigureAwait(false);

        return MatchesRoleGrant(principal, grants);
    }

    public Task<bool> HasModuleAccessAsync(
        ClaimsPrincipal principal,
        string moduleKey,
        string minimumAccessLevel = AccessLevels.View,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(moduleKey))
            throw new IdentityValidationException("moduleKey is required.");

        return HasResourceAccessAsync(
            principal,
            AccessResourceTypes.Module,
            moduleKey,
            minimumAccessLevel,
            ct);
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

        var tenantId = ResolveTenantId(principal);
        if (tenantId == Guid.Empty || principal?.Identity?.IsAuthenticated != true)
            return false;

        if (HasUnrestrictedAccess(principal, ownerOnly: false))
            return true;

        var subject = ResolveSubject(principal);
        if (subject.SubjectId.Length == 0)
            return false;

        var directGrants = await _grants
            .GetGrantsAsync(tenantId, subject.SubjectType, subject.SubjectId, ct)
            .ConfigureAwait(false);

        if (directGrants.Any(x =>
            IsActiveMatch(x, resourceType, resourceId) &&
            MeetsMinimumAccess(x.AccessLevel, minimumAccessLevel)))
            return true;

        var roleNames = GetRoleNames(principal).ToList();
        var roleIds = GetRoleIds(principal).ToList();
        var permissionNames = await ResolveGrantedPermissionNamesAsync(principal, tenantId, ct).ConfigureAwait(false);
        if (MatchesStaticModuleDefinition(resourceType, resourceId, minimumAccessLevel, roleNames, roleIds, permissionNames))
            return true;

        if (!_ruleProviders.Any())
            return false;

        var context = new AccessEvaluationContext(
            tenantId,
            principal,
            subject.SubjectType,
            subject.SubjectId,
            resourceType.Trim(),
            resourceId.Trim(),
            minimumAccessLevel.Trim(),
            roleNames,
            roleIds,
            permissionNames,
            directGrants);

        foreach (var provider in _ruleProviders)
        {
            var decisions = await provider.EvaluateAsync(context, ct).ConfigureAwait(false);
            if (decisions.Any(x => x.IsAllowed))
                return true;
        }

        return false;
    }

    public async Task RequirePermissionAsync(ClaimsPrincipal principal, string permissionName, CancellationToken ct = default)
    {
        if (!await HasPermissionAsync(principal, permissionName, ct).ConfigureAwait(false))
            throw new IdentityUnauthorizedException($"Permission '{permissionName}' is required.");
    }

    public async Task RequireModuleAccessAsync(
        ClaimsPrincipal principal,
        string moduleKey,
        string minimumAccessLevel = AccessLevels.View,
        CancellationToken ct = default)
    {
        if (!await HasModuleAccessAsync(principal, moduleKey, minimumAccessLevel, ct).ConfigureAwait(false))
            throw new IdentityUnauthorizedException($"Module access '{moduleKey}:{minimumAccessLevel}' is required.");
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

    public async Task<AccessCatalogDto> GetAccessCatalogAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            throw new IdentityValidationException("tenantId is required.");

        var options = _options.CurrentValue;
        var resources = options.Modules
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .Select(x => new AccessCatalogResource(
                AccessResourceTypes.Module,
                x.Key.Trim(),
                string.IsNullOrWhiteSpace(x.Label) ? x.Key.Trim() : x.Label.Trim(),
                x.Description,
                NormalizeAccessLevels(x.SupportedAccessLevels)))
            .ToList();

        foreach (var provider in _catalogProviders)
        {
            var provided = await provider.GetResourcesAsync(tenantId, ct).ConfigureAwait(false);
            resources.AddRange(provided.Where(x =>
                !string.IsNullOrWhiteSpace(x.ResourceType) &&
                !string.IsNullOrWhiteSpace(x.ResourceId)));
        }

        var accessLevels = OrderedAccessLevels().Select(x => x.Key).ToList();
        var resourceTypes = resources
            .Select(x => x.ResourceType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AccessCatalogDto(
            resourceTypes,
            accessLevels,
            resources
                .GroupBy(x => $"{x.ResourceType}|{x.ResourceId}", StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderBy(x => x.ResourceType, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    public async Task<AccessContextDto> GetCurrentAccessContextAsync(
        ClaimsPrincipal principal,
        Guid? tenantId = null,
        CancellationToken ct = default)
    {
        if (principal?.Identity?.IsAuthenticated != true)
            throw new IdentityUnauthorizedException("Authenticated user is required.");

        var resolvedTenantId = tenantId.GetValueOrDefault(ResolveTenantId(principal));
        if (resolvedTenantId == Guid.Empty)
            throw new IdentityValidationException("tenantId is required.");

        var subject = ResolveSubject(principal);
        if (subject.SubjectId.Length == 0)
            throw new IdentityValidationException("The current principal does not include a subject identifier.");

        var roleNames = GetRoleNames(principal).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        var roleIds = GetRoleIds(principal).OrderBy(x => x).ToList();
        var permissions = await ResolveGrantedPermissionNamesAsync(principal, resolvedTenantId, ct).ConfigureAwait(false);
        var directGrants = await _grants
            .GetGrantsAsync(resolvedTenantId, subject.SubjectType, subject.SubjectId, ct)
            .ConfigureAwait(false);

        var modules = BuildModuleContext(
            roleNames,
            roleIds,
            permissions,
            directGrants,
            HasUnrestrictedAccess(principal, ownerOnly: false));
        var resources = directGrants
            .Where(x => x.IsActive && !IsModuleResource(x))
            .GroupBy(x => x.ResourceType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<AccessContextResourceDto>)x
                    .GroupBy(g => g.ResourceId, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderByDescending(grant => AccessRank(grant.AccessLevel)).First())
                    .Select(g => new AccessContextResourceDto(g.ResourceId, g.AccessLevel, "grant"))
                    .OrderBy(g => g.ResourceId, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

        var canManageUsers = await HasPermissionAsync(principal, "users.manage", ct).ConfigureAwait(false);
        var canManageRoles = await HasPermissionAsync(principal, "roles.manage", ct).ConfigureAwait(false);
        var canManageAccess = await HasPermissionAsync(principal, "access.manage", ct).ConfigureAwait(false);

        var capabilities = new AccessCapabilitiesDto(
            CanManageUsers: canManageUsers,
            CanManageRoles: canManageRoles,
            CanManageAccess: canManageAccess,
            CanAssignOwner: HasOwnerRole(principal));

        return new AccessContextDto(
            subject.SubjectId,
            resolvedTenantId,
            roleNames,
            roleIds,
            permissions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            modules,
            resources,
            capabilities);
    }

    public async Task<AccessDecision> CheckAccessAsync(
        ClaimsPrincipal principal,
        Guid tenantId,
        AccessCheckRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        if (tenantId == Guid.Empty)
            throw new IdentityValidationException("tenantId is required.");

        var requestedSubjectType = NormalizeRequired(request.SubjectType, "subjectType");
        var requestedSubjectId = NormalizeRequired(request.SubjectId, "subjectId");
        var currentSubject = ResolveSubject(principal);

        if (string.Equals(currentSubject.SubjectType, requestedSubjectType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(currentSubject.SubjectId, requestedSubjectId, StringComparison.OrdinalIgnoreCase) &&
            HasUnrestrictedAccess(principal, ownerOnly: false))
        {
            return new AccessDecision(true, "role", "Caller has unrestricted tenant access.", request.AccessLevel);
        }

        var directGrants = await _grants
            .GetGrantsAsync(tenantId, requestedSubjectType, requestedSubjectId, ct)
            .ConfigureAwait(false);

        if (directGrants.Any(x =>
            IsActiveMatch(x, request.ResourceType, request.ResourceId) &&
            MeetsMinimumAccess(x.AccessLevel, request.AccessLevel)))
            return new AccessDecision(true, "grant", null, request.AccessLevel);

        var context = new AccessEvaluationContext(
            tenantId,
            principal,
            requestedSubjectType,
            requestedSubjectId,
            NormalizeRequired(request.ResourceType, "resourceType"),
            NormalizeRequired(request.ResourceId, "resourceId"),
            NormalizeRequired(request.AccessLevel, "accessLevel"),
            Array.Empty<string>(),
            Array.Empty<Guid>(),
            Array.Empty<string>(),
            directGrants);

        foreach (var provider in _ruleProviders)
        {
            var decisions = await provider.EvaluateAsync(context, ct).ConfigureAwait(false);
            var allowedDecision = decisions.FirstOrDefault(x => x.IsAllowed);
            if (allowedDecision is not null)
                return allowedDecision;
        }

        return new AccessDecision(false, "none", null, request.AccessLevel);
    }

    private IReadOnlyList<AccessContextModuleDto> BuildModuleContext(
        IReadOnlyList<string> roleNames,
        IReadOnlyList<Guid> roleIds,
        IReadOnlyList<string> permissionNames,
        IReadOnlyList<AccessGrant> grants,
        bool hasUnrestrictedAccess)
    {
        var modules = new Dictionary<string, AccessContextModuleDto>(StringComparer.OrdinalIgnoreCase);

        if (hasUnrestrictedAccess)
        {
            foreach (var module in _options.CurrentValue.Modules.Where(x => !string.IsNullOrWhiteSpace(x.Key)))
                AddOrUpgrade(modules, module.Key, BestDefinedAccessLevel(module), "role");
        }

        foreach (var grant in grants.Where(x => x.IsActive && IsModuleResource(x)))
        {
            AddOrUpgrade(modules, grant.ResourceId, grant.AccessLevel, "grant");
        }

        foreach (var module in _options.CurrentValue.Modules.Where(x => !string.IsNullOrWhiteSpace(x.Key)))
        {
            if (ModuleDefinitionMatches(module, roleNames, roleIds, permissionNames))
                AddOrUpgrade(modules, module.Key, BestDefinedAccessLevel(module), "role");
        }

        return modules.Values
            .OrderBy(x => x.Module, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void AddOrUpgrade(Dictionary<string, AccessContextModuleDto> modules, string moduleKey, string accessLevel, string source)
    {
        if (string.IsNullOrWhiteSpace(moduleKey))
            return;

        var key = moduleKey.Trim();
        if (modules.TryGetValue(key, out var existing) && AccessRank(existing.AccessLevel) >= AccessRank(accessLevel))
            return;

        modules[key] = new AccessContextModuleDto(key, accessLevel.Trim(), source);
    }

    private async Task<IReadOnlyList<string>> ResolveGrantedPermissionNamesAsync(
        ClaimsPrincipal principal,
        Guid tenantId,
        CancellationToken ct)
    {
        if (HasUnrestrictedAccess(principal, ownerOnly: false))
            return (await _catalog.GetExposedPermissionsAsync(ct).ConfigureAwait(false))
                .Where(x => !string.IsNullOrWhiteSpace(x.PermissionName))
                .Select(x => x.PermissionName!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        var permissions = await _catalog.GetExposedPermissionsAsync(ct).ConfigureAwait(false);
        var roleNames = GetRoleNames(principal);
        var roleIds = GetRoleIds(principal);
        var granted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var permission in permissions)
        {
            var names = string.IsNullOrWhiteSpace(permission.PermissionName)
                ? Array.Empty<string>()
                : new[] { permission.PermissionName.Trim() };
            var ids = permission.PermissionId.HasValue
                ? new[] { permission.PermissionId.Value }
                : Array.Empty<Guid>();

            var permissionGrants = await _permissionGrantResolver
                .ResolveAsync(tenantId, names, ids, ct)
                .ConfigureAwait(false);

            if (permissionGrants.RoleNames.Any(roleNames.Contains) ||
                permissionGrants.RoleIds.Any(roleIds.Contains))
            {
                if (!string.IsNullOrWhiteSpace(permission.PermissionName))
                    granted.Add(permission.PermissionName.Trim());
            }
        }

        return granted.ToList();
    }

    private bool MatchesStaticModuleDefinition(
        string resourceType,
        string resourceId,
        string minimumAccessLevel,
        IReadOnlyList<string> roleNames,
        IReadOnlyList<Guid> roleIds,
        IReadOnlyList<string> permissionNames)
    {
        if (!string.Equals(resourceType, AccessResourceTypes.Module, StringComparison.OrdinalIgnoreCase))
            return false;

        return _options.CurrentValue.Modules
            .Where(x => string.Equals(x.Key, resourceId, StringComparison.OrdinalIgnoreCase))
            .Any(x =>
                MeetsMinimumAccess(BestDefinedAccessLevel(x), minimumAccessLevel) &&
                ModuleDefinitionMatches(x, roleNames, roleIds, permissionNames));
    }

    private static bool ModuleDefinitionMatches(
        AccessModuleDefinition module,
        IReadOnlyList<string> roleNames,
        IReadOnlyList<Guid> roleIds,
        IReadOnlyList<string> permissionNames)
    {
        return (module.ImpliedByRoleNames?.Any(roleNames.Contains) == true) ||
               (module.ImpliedByRoleIds?.Any(roleIds.Contains) == true) ||
               (module.ImpliedByPermissionNames?.Any(permissionNames.Contains) == true);
    }

    private string BestDefinedAccessLevel(AccessModuleDefinition module)
    {
        var supported = NormalizeAccessLevels(module.SupportedAccessLevels);
        if (supported.Count == 0)
            return AccessLevels.View;

        return supported
            .OrderByDescending(AccessRank)
            .First();
    }

    private IReadOnlyList<string> NormalizeAccessLevels(IReadOnlyList<string>? levels)
        => (levels is { Count: > 0 } ? levels : OrderedAccessLevels().Select(x => x.Key))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private bool MeetsMinimumAccess(string grantedAccessLevel, string minimumAccessLevel)
        => AccessRank(grantedAccessLevel) >= AccessRank(minimumAccessLevel);

    private int AccessRank(string? accessLevel)
    {
        if (string.IsNullOrWhiteSpace(accessLevel))
            return 0;

        var match = OrderedAccessLevels()
            .FirstOrDefault(x => string.Equals(x.Key, accessLevel.Trim(), StringComparison.OrdinalIgnoreCase));

        return match?.Rank ?? 0;
    }

    private IReadOnlyList<AccessLevelDefinition> OrderedAccessLevels()
        => _options.CurrentValue.AccessLevels
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.Rank)
            .ToList();

    private bool HasUnrestrictedAccess(ClaimsPrincipal principal, bool ownerOnly)
    {
        var options = _options.CurrentValue;
        if (options.OwnerHasUnrestrictedTenantAccess && HasAnyConfiguredRole(principal, options.OwnerRoleNames))
            return true;

        return !ownerOnly &&
               options.AdminHasUnrestrictedAccessExceptOwnerActions &&
               HasAnyConfiguredRole(principal, options.AdminRoleNames);
    }

    private bool HasOwnerRole(ClaimsPrincipal principal)
        => HasAnyConfiguredRole(principal, _options.CurrentValue.OwnerRoleNames);

    private static bool HasAnyConfiguredRole(ClaimsPrincipal principal, IEnumerable<string> configuredNames)
    {
        var roleNames = GetRoleNames(principal);
        return configuredNames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Any(roleNames.Contains);
    }

    private static bool MatchesRoleGrant(ClaimsPrincipal principal, PermissionGrantSet grants)
    {
        var roleNames = GetRoleNames(principal);
        var roleIds = GetRoleIds(principal);

        return grants.RoleNames.Any(roleNames.Contains) ||
               grants.RoleIds.Any(roleIds.Contains);
    }

    private static bool IsActiveMatch(AccessGrant grant, string resourceType, string resourceId)
        => grant.IsActive &&
           string.Equals(grant.ResourceType, resourceType.Trim(), StringComparison.OrdinalIgnoreCase) &&
           string.Equals(grant.ResourceId, resourceId.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsModuleResource(AccessGrant grant)
        => string.Equals(grant.ResourceType, AccessResourceTypes.Module, StringComparison.OrdinalIgnoreCase);

    private static Guid ResolveTenantId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirst("tid")?.Value ??
                  principal.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(raw, out var tenantId) ? tenantId : Guid.Empty;
    }

    private static (string SubjectType, string SubjectId) ResolveSubject(ClaimsPrincipal principal)
    {
        var credentialId = principal.FindFirst("credential_id")?.Value ??
                           principal.FindFirst("api_credential_id")?.Value;
        if (!string.IsNullOrWhiteSpace(credentialId))
            return (AccessSubjectTypes.ApiCredential, credentialId.Trim());

        foreach (var claimType in UserIdClaimTypes)
        {
            var value = principal.FindFirst(claimType)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
                return (AccessSubjectTypes.User, value.Trim());
        }

        return (AccessSubjectTypes.User, string.Empty);
    }

    private static string NormalizeRequired(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new IdentityValidationException($"{name} is required.");

        return value.Trim();
    }

    private static HashSet<string> GetRoleNames(ClaimsPrincipal principal)
        => principal.Claims
            .Where(x =>
                string.Equals(x.Type, "role", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Type, ClaimTypes.Role, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static HashSet<Guid> GetRoleIds(ClaimsPrincipal principal)
        => principal.Claims
            .Where(x =>
                string.Equals(x.Type, "rid", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Type, "role_id", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .Where(x => Guid.TryParse(x, out _))
            .Select(Guid.Parse)
            .ToHashSet();
}
