using System.Security.Claims;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using Microsoft.Extensions.Options;
using ResourceAccessGrantRecord = IBeam.AccessControl.ResourceAccessGrantRecord;
using IResourceAccessStore = IBeam.AccessControl.IResourceAccessStore;

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

    private readonly IResourceAccessStore _grants;
    private readonly IIBeamAccessCatalogOverrideStore _catalogOverrides;
    private readonly IPermissionCatalogProvider _catalog;
    private readonly IIBeamOperationCatalogProvider _operationCatalog;
    private readonly IApiCredentialScopeCatalogProvider _apiScopeCatalog;
    private readonly IPermissionGrantResolver _permissionGrantResolver;
    private readonly IOptionsMonitor<IBeamAccessControlOptions> _options;
    private readonly IEnumerable<IIBeamAccessCatalogProvider> _catalogProviders;
    private readonly IEnumerable<IIBeamAccessCatalogItemProvider> _catalogItemProviders;
    private readonly IEnumerable<IAgentCatalogProvider> _agentCatalogProviders;
    private readonly IEnumerable<IIBeamAccessRuleProvider> _ruleProviders;

    public IBeamAccessControlService(
        IResourceAccessStore grants,
        IIBeamAccessCatalogOverrideStore catalogOverrides,
        IPermissionCatalogProvider catalog,
        IIBeamOperationCatalogProvider operationCatalog,
        IApiCredentialScopeCatalogProvider apiScopeCatalog,
        IPermissionGrantResolver permissionGrantResolver,
        IOptionsMonitor<IBeamAccessControlOptions> options,
        IEnumerable<IIBeamAccessCatalogProvider> catalogProviders,
        IEnumerable<IIBeamAccessCatalogItemProvider> catalogItemProviders,
        IEnumerable<IAgentCatalogProvider> agentCatalogProviders,
        IEnumerable<IIBeamAccessRuleProvider> ruleProviders)
    {
        _grants = grants ?? throw new ArgumentNullException(nameof(grants));
        _catalogOverrides = catalogOverrides ?? throw new ArgumentNullException(nameof(catalogOverrides));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _operationCatalog = operationCatalog ?? throw new ArgumentNullException(nameof(operationCatalog));
        _apiScopeCatalog = apiScopeCatalog ?? throw new ArgumentNullException(nameof(apiScopeCatalog));
        _permissionGrantResolver = permissionGrantResolver ?? throw new ArgumentNullException(nameof(permissionGrantResolver));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _catalogProviders = catalogProviders ?? throw new ArgumentNullException(nameof(catalogProviders));
        _catalogItemProviders = catalogItemProviders ?? throw new ArgumentNullException(nameof(catalogItemProviders));
        _agentCatalogProviders = agentCatalogProviders ?? throw new ArgumentNullException(nameof(agentCatalogProviders));
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

        var directGrants = await GetGrantsForSubjectAsync(tenantId, subject.SubjectType, subject.SubjectId, ct)
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

        var items = await BuildEffectiveCatalogItemsAsync(tenantId, includeOverrides: true, ct).ConfigureAwait(false);
        var resources = ItemsForCategory(items, AccessCatalogCategories.Resource);

        return new AccessCatalogDto(
            Permissions: ItemsForCategory(items, AccessCatalogCategories.Permission),
            Operations: ItemsForCategory(items, AccessCatalogCategories.Operation),
            Modules: ItemsForCategory(items, AccessCatalogCategories.Module),
            ApiScopes: ItemsForCategory(items, AccessCatalogCategories.ApiScope),
            Tools: ItemsForCategory(items, AccessCatalogCategories.Tool),
            Agents: ItemsForCategory(items, AccessCatalogCategories.Agent),
            Resources: resources,
            AccessLevels: ItemsForCategory(items, AccessCatalogCategories.AccessLevel),
            ResourceTypes: resources
                .Select(x => x.ResourceType)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    public Task<IReadOnlyList<AccessOperationCatalogItem>> GetOperationCatalogAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            throw new IdentityValidationException("tenantId is required.");

        return _operationCatalog.GetOperationsAsync(tenantId, ct);
    }

    public Task<IReadOnlyList<AccessCatalogOverride>> GetAccessCatalogOverridesAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            throw new IdentityValidationException("tenantId is required.");

        return _catalogOverrides.GetOverridesAsync(tenantId, ct);
    }

    public async Task<AccessCatalogOverride> UpsertAccessCatalogOverrideAsync(
        Guid tenantId,
        Guid? catalogItemId,
        UpsertAccessCatalogOverrideRequest request,
        CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            throw new IdentityValidationException("tenantId is required.");
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var key = NormalizeRequired(request.Key, "key");
        var category = NormalizeRequired(request.Category, "category");
        var label = NormalizeRequired(request.Label, "label");
        var normalized = new UpsertAccessCatalogOverrideRequest
        {
            Key = key,
            Label = label,
            Description = NormalizeOptional(request.Description),
            Category = category,
            IsAssignable = request.IsAssignable,
            IsMutable = request.IsMutable,
            IsEnabled = request.IsEnabled,
            SubjectTypes = NormalizeList(request.SubjectTypes).ToList(),
            ResourceType = NormalizeOptional(request.ResourceType),
            ResourceId = NormalizeOptional(request.ResourceId),
            ParentResourceType = NormalizeOptional(request.ParentResourceType),
            ParentResourceId = NormalizeOptional(request.ParentResourceId),
            SupportedAccessLevels = NormalizeList(request.SupportedAccessLevels).ToList(),
            Rank = request.Rank,
            ModuleKey = NormalizeOptional(request.ModuleKey),
            RequiredAccessLevel = NormalizeOptional(request.RequiredAccessLevel),
            IsDangerous = request.IsDangerous,
            IdParameter = NormalizeOptional(request.IdParameter)
        };

        if (catalogItemId is null)
        {
            var baseItems = await BuildEffectiveCatalogItemsAsync(tenantId, includeOverrides: false, ct).ConfigureAwait(false);
            var existing = baseItems.FirstOrDefault(x =>
                string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));

            if (existing is not null && !existing.IsMutable)
                throw new IdentityValidationException($"Catalog item '{category}:{key}' is not mutable.");
        }

        return await _catalogOverrides.UpsertOverrideAsync(tenantId, catalogItemId, normalized, ct).ConfigureAwait(false);
    }

    public Task DeleteAccessCatalogOverrideAsync(Guid tenantId, Guid catalogItemId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            throw new IdentityValidationException("tenantId is required.");
        if (catalogItemId == Guid.Empty)
            throw new IdentityValidationException("catalogItemId is required.");

        return _catalogOverrides.DeleteOverrideAsync(tenantId, catalogItemId, ct);
    }

    private async Task<IReadOnlyList<AccessCatalogItem>> BuildEffectiveCatalogItemsAsync(
        Guid tenantId,
        bool includeOverrides,
        CancellationToken ct)
    {
        var options = _options.CurrentValue;
        var items = new List<AccessCatalogItem>();

        items.AddRange(options.AccessLevels
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .Select(x => new AccessCatalogItem(
                x.Key.Trim(),
                string.IsNullOrWhiteSpace(x.Label) ? x.Key.Trim() : x.Label.Trim(),
                null,
                AccessCatalogCategories.AccessLevel,
                IsBuiltInAccessLevel(x) ? AccessCatalogSources.IBeamDefault : AccessCatalogSources.HostConfig,
                true,
                false,
                true,
                SupportedAccessLevels: null,
                Rank: x.Rank)));

        items.AddRange(options.Modules
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .Select(x => new AccessCatalogItem(
                x.Key.Trim(),
                string.IsNullOrWhiteSpace(x.Label) ? x.Key.Trim() : x.Label.Trim(),
                x.Description,
                AccessCatalogCategories.Module,
                AccessCatalogSources.HostConfig,
                true,
                false,
                true,
                SubjectTypes: [AccessSubjectTypes.User, AccessSubjectTypes.ApiCredential],
                ResourceType: AccessResourceTypes.Module,
                ResourceId: x.Key.Trim(),
                SupportedAccessLevels: NormalizeAccessLevels(x.SupportedAccessLevels))));

        var permissions = await _catalog.GetExposedPermissionsAsync(ct).ConfigureAwait(false);
        items.AddRange(permissions
            .Where(x => !string.IsNullOrWhiteSpace(x.PermissionName))
            .Select(x => new AccessCatalogItem(
                x.PermissionName!.Trim(),
                string.IsNullOrWhiteSpace(x.Label) ? x.PermissionName!.Trim() : x.Label!.Trim(),
                x.Description,
                AccessCatalogCategories.Permission,
                NormalizePermissionSource(x.Source),
                x.IsAssignable,
                false,
                true,
                SubjectTypes: [AccessSubjectTypes.User],
                ResourceType: x.ResourceType,
                ResourceId: x.ResourceId,
                SupportedAccessLevels: string.IsNullOrWhiteSpace(x.AccessLevel) ? null : [x.AccessLevel.Trim()],
                ModuleKey: x.ModuleKey,
                RequiredAccessLevel: x.AccessLevel)));

        var operations = await _operationCatalog.GetOperationsAsync(tenantId, ct).ConfigureAwait(false);
        items.AddRange(operations.Select(x => new AccessCatalogItem(
            x.Key,
            x.Label,
            x.Description,
            AccessCatalogCategories.Operation,
            NormalizeOperationSource(x.Source),
            x.IsAssignable,
            false,
            true,
            SubjectTypes: [AccessSubjectTypes.User, AccessSubjectTypes.ApiCredential],
            ResourceType: x.ResourceType,
            SupportedAccessLevels: string.IsNullOrWhiteSpace(x.RequiredAccessLevel) ? null : [x.RequiredAccessLevel],
            ModuleKey: x.ModuleKey,
            RequiredAccessLevel: x.RequiredAccessLevel,
            IsDangerous: x.IsDangerous,
            IdParameter: x.IdParameter)));

        var apiScopes = await _apiScopeCatalog.GetScopesAsync(tenantId, ct).ConfigureAwait(false);
        items.AddRange(apiScopes.Select(x => new AccessCatalogItem(
            x.Key,
            x.DisplayName,
            x.Description,
            string.Equals(x.Category, "tool", StringComparison.OrdinalIgnoreCase)
                ? AccessCatalogCategories.Tool
                : AccessCatalogCategories.ApiScope,
            AccessCatalogSources.IBeamDefault,
            x.IsAssignable,
            false,
            true,
            SubjectTypes: [AccessSubjectTypes.ApiCredential],
            ResourceType: x.ResourceType,
            ResourceId: x.ModuleKey ?? x.Key,
            SupportedAccessLevels: null)));

        foreach (var provider in _agentCatalogProviders)
        {
            var agents = await provider.GetAgentsAsync(tenantId, ct).ConfigureAwait(false);
            items.AddRange(agents
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .Select(x => new AccessCatalogItem(
                    x.Key.Trim(),
                    string.IsNullOrWhiteSpace(x.DisplayName) ? x.Key.Trim() : x.DisplayName.Trim(),
                    x.Description,
                    AccessCatalogCategories.Agent,
                    AccessCatalogSources.HostProvider,
                    x.IsAssignable,
                    false,
                    true,
                    SubjectTypes: [AccessSubjectTypes.ApiCredential])));
        }

        foreach (var provider in _catalogProviders)
        {
            var provided = await provider.GetResourcesAsync(tenantId, ct).ConfigureAwait(false);
            items.AddRange(provided
                .Where(x => !string.IsNullOrWhiteSpace(x.ResourceType) && !string.IsNullOrWhiteSpace(x.ResourceId))
                .Select(x => new AccessCatalogItem(
                    $"{x.ResourceType.Trim()}:{x.ResourceId.Trim()}",
                    x.Label,
                    x.Description,
                    AccessCatalogCategories.Resource,
                    string.IsNullOrWhiteSpace(x.Source) ? AccessCatalogSources.HostProvider : x.Source.Trim(),
                    x.IsAssignable,
                    x.IsMutable,
                    x.IsEnabled,
                    SubjectTypes: [AccessSubjectTypes.User, AccessSubjectTypes.ApiCredential],
                    ResourceType: x.ResourceType.Trim(),
                    ResourceId: x.ResourceId.Trim(),
                    ParentResourceType: NormalizeOptional(x.ParentResourceType),
                    ParentResourceId: NormalizeOptional(x.ParentResourceId),
                    SupportedAccessLevels: NormalizeAccessLevels(x.SupportedAccessLevels))));
        }

        foreach (var provider in _catalogItemProviders)
        {
            var provided = await provider.GetCatalogItemsAsync(tenantId, ct).ConfigureAwait(false);
            items.AddRange(provided
                .Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Category))
                .Select(x => x with
                {
                    Key = x.Key.Trim(),
                    Category = x.Category.Trim(),
                    Source = string.IsNullOrWhiteSpace(x.Source) ? AccessCatalogSources.HostProvider : x.Source.Trim()
                }));
        }

        if (includeOverrides)
        {
            var baseKeys = items
                .Select(CatalogIdentity)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var overrides = await _catalogOverrides.GetOverridesAsync(tenantId, ct).ConfigureAwait(false);
            items.AddRange(overrides.Select(x =>
            {
                var identity = CatalogIdentity(x.Category, x.Key);
                var source = baseKeys.Contains(identity)
                    ? AccessCatalogSources.TenantOverride
                    : AccessCatalogSources.TenantDb;

                return new AccessCatalogItem(
                    x.Key,
                    x.Label,
                    x.Description,
                    x.Category,
                    source,
                    x.IsAssignable,
                    x.IsMutable,
                    x.IsEnabled,
                    x.SubjectTypes,
                    x.ResourceType,
                    x.ResourceId,
                    x.ParentResourceType,
                    x.ParentResourceId,
                    x.SupportedAccessLevels,
                    x.Rank,
                    x.ModuleKey,
                    x.RequiredAccessLevel,
                    x.IsDangerous,
                    x.IdParameter);
            }));
        }

        return items
            .Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Category))
            .GroupBy(CatalogIdentity, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderBy(SourceOrder).Last())
            .Where(x => x.IsEnabled)
            .OrderBy(x => CategoryOrder(x.Category))
            .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
        var directGrants = await GetGrantsForSubjectAsync(resolvedTenantId, subject.SubjectType, subject.SubjectId, ct)
            .ConfigureAwait(false);

        var modules = BuildModuleContext(
            roleNames,
            roleIds,
            permissions,
            directGrants,
            HasUnrestrictedAccess(principal, ownerOnly: false));
        var resources = directGrants
            .Where(x => IsActive(x) && !IsModuleResource(x))
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

        var directGrants = await GetGrantsForSubjectAsync(tenantId, requestedSubjectType, requestedSubjectId, ct)
            .ConfigureAwait(false);

        if (directGrants.Any(x =>
            IsActiveMatch(x, request.ResourceType, request.ResourceId) &&
            MeetsMinimumAccess(x.AccessLevel, EffectiveAccessLevel(request))))
            return new AccessDecision(true, "grant", null, EffectiveAccessLevel(request));

        var context = new AccessEvaluationContext(
            tenantId,
            principal,
            requestedSubjectType,
            requestedSubjectId,
            NormalizeRequired(request.ResourceType, "resourceType"),
            NormalizeRequired(request.ResourceId, "resourceId"),
            NormalizeRequired(EffectiveAccessLevel(request), "accessLevel"),
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

        return new AccessDecision(false, "none", null, EffectiveAccessLevel(request));
    }

    private static string EffectiveAccessLevel(AccessCheckRequest request)
        => string.IsNullOrWhiteSpace(request.MinimumAccessLevel)
            ? request.AccessLevel
            : request.MinimumAccessLevel.Trim();

    private static IReadOnlyList<AccessCatalogItem> ItemsForCategory(
        IReadOnlyList<AccessCatalogItem> items,
        string category)
        => items
            .Where(x => string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static string NormalizePermissionSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return AccessCatalogSources.HostConfig;

        var value = source.Trim();
        if (value.StartsWith("configuration", StringComparison.OrdinalIgnoreCase))
            return AccessCatalogSources.HostConfig;
        if (value.StartsWith("attribute", StringComparison.OrdinalIgnoreCase))
            return AccessCatalogSources.HostProvider;
        if (value.StartsWith("mapping", StringComparison.OrdinalIgnoreCase))
            return AccessCatalogSources.HostConfig;

        return value;
    }

    private static bool IsBuiltInAccessLevel(AccessLevelDefinition level)
        => (string.Equals(level.Key, AccessLevels.View, StringComparison.OrdinalIgnoreCase) && level.Rank == 0) ||
           (string.Equals(level.Key, AccessLevels.Edit, StringComparison.OrdinalIgnoreCase) && level.Rank == 10) ||
           (string.Equals(level.Key, AccessLevels.Manage, StringComparison.OrdinalIgnoreCase) && level.Rank == 20);

    private static int CategoryOrder(string category)
        => category.ToLowerInvariant() switch
        {
            AccessCatalogCategories.Permission => 1,
            AccessCatalogCategories.Operation => 2,
            AccessCatalogCategories.Module => 3,
            AccessCatalogCategories.ApiScope => 4,
            AccessCatalogCategories.Tool => 5,
            AccessCatalogCategories.Agent => 6,
            AccessCatalogCategories.Resource => 7,
            AccessCatalogCategories.AccessLevel => 8,
            _ => 100
        };

    private static int SourceOrder(AccessCatalogItem item)
        => item.Source.Trim().ToLowerInvariant() switch
        {
            AccessCatalogSources.IBeamDefault => 0,
            AccessCatalogSources.HostConfig => 1,
            AccessCatalogSources.HostProvider => 2,
            AccessCatalogSources.TenantDb => 3,
            AccessCatalogSources.TenantOverride => 4,
            "attribute" => 2,
            "attribute:template" => 2,
            _ => 1
        };

    private static string NormalizeOperationSource(string? source)
        => string.IsNullOrWhiteSpace(source)
            ? AccessCatalogSources.HostProvider
            : source.StartsWith("attribute", StringComparison.OrdinalIgnoreCase)
                ? AccessCatalogSources.HostProvider
                : source.Trim();

    private static string CatalogIdentity(AccessCatalogItem item)
        => CatalogIdentity(item.Category, item.Key);

    private static string CatalogIdentity(string category, string key)
        => $"{category.Trim()}|{key.Trim()}";

    private static IReadOnlyList<string> NormalizeList(IEnumerable<string>? values)
        => (values ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private IReadOnlyList<AccessContextModuleDto> BuildModuleContext(
        IReadOnlyList<string> roleNames,
        IReadOnlyList<Guid> roleIds,
        IReadOnlyList<string> permissionNames,
        IReadOnlyList<ResourceAccessGrantRecord> grants,
        bool hasUnrestrictedAccess)
    {
        var modules = new Dictionary<string, AccessContextModuleDto>(StringComparer.OrdinalIgnoreCase);

        if (hasUnrestrictedAccess)
        {
            foreach (var module in _options.CurrentValue.Modules.Where(x => !string.IsNullOrWhiteSpace(x.Key)))
                AddOrUpgrade(modules, module.Key, BestDefinedAccessLevel(module), "role");
        }

        foreach (var grant in grants.Where(x => IsActive(x) && IsModuleResource(x)))
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

    private static bool MatchesRoleGrant(ClaimsPrincipal principal, IBeam.AccessControl.PermissionGrantSet grants)
    {
        var roleNames = GetRoleNames(principal);
        var roleIds = GetRoleIds(principal);

        return grants.RoleNames.Any(roleNames.Contains) ||
               grants.RoleIds.Any(roleIds.Contains);
    }

    private async Task<IReadOnlyList<ResourceAccessGrantRecord>> GetGrantsForSubjectAsync(
        Guid tenantId,
        string subjectType,
        string subjectId,
        CancellationToken ct)
    {
        var grants = await _grants.ListGrantsAsync(tenantId, ct).ConfigureAwait(false);
        return grants
            .Where(x => string.Equals(x.Subject.SubjectType, subjectType, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.Equals(x.Subject.SubjectId, subjectId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static bool IsActiveMatch(ResourceAccessGrantRecord grant, string resourceType, string resourceId)
        => IsActive(grant) &&
           string.Equals(grant.ResourceType, resourceType.Trim(), StringComparison.OrdinalIgnoreCase) &&
           string.Equals(grant.ResourceId, resourceId.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsActive(ResourceAccessGrantRecord grant)
        => grant.IsActive(DateTimeOffset.UtcNow);

    private static bool IsModuleResource(ResourceAccessGrantRecord grant)
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
