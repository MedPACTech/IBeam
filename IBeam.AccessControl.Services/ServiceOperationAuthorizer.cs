using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace IBeam.AccessControl.Services;

public sealed class ServiceOperationAuthorizer : IServiceOperationAuthorizer
{
    private readonly IEnumerable<IServiceOperationPermissionRuleProvider> _providers;
    private readonly IOptionsMonitor<ServiceOperationAuthorizationOptions> _options;
    private readonly IPermissionRoleAuthorizer? _permissionRoleAuthorizer;

    public ServiceOperationAuthorizer(
        IEnumerable<IServiceOperationPermissionRuleProvider> providers,
        IOptionsMonitor<ServiceOperationAuthorizationOptions> options,
        IPermissionRoleAuthorizer? permissionRoleAuthorizer = null)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _permissionRoleAuthorizer = permissionRoleAuthorizer;
    }

    public async Task<ServiceOperationAuthorizationResult> AuthorizeAsync(
        ServiceOperationAuthorizationRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ServiceOperationPermissionService.ValidateTenantId(request.TenantId);
        var operationName = ServiceOperationPermissionService.NormalizePattern(request.OperationName);

        var options = _options.CurrentValue;
        if (!options.Enabled)
        {
            return ServiceOperationAuthorizationResult.Allow(operationName, "service-operation-authorization-disabled");
        }

        if (request.Principal?.Identity?.IsAuthenticated != true)
        {
            return ServiceOperationAuthorizationResult.Deny(operationName, "principal-not-authenticated");
        }

        var context = ServiceOperationSubjectContext.FromPrincipal(request.Principal);
        var rules = new List<ServiceOperationPermissionRule>();
        foreach (var provider in _providers)
        {
            var provided = await provider.ListRulesAsync(request.TenantId, ct).ConfigureAwait(false);
            rules.AddRange(provided);
        }

        var matched = rules
            .Where(x => x.IsActive)
            .Where(x => TenantMatches(x, request.TenantId))
            .Select(x => new RuleMatch(x, PatternSpecificity(x.Pattern, operationName)))
            .Where(x => x.Specificity >= 0)
            .Where(x => SubjectMatches(x.Rule, context.SubjectType))
            .Where(x => RoleMatches(x.Rule, context.RoleNames, context.RoleIds))
            .OrderByDescending(x => SourcePrecedence(x.Rule.Source))
            .ThenByDescending(x => x.Specificity)
            .ThenByDescending(x => x.Rule.Priority)
            .ThenByDescending(x => IsDeny(x.Rule) ? 1 : 0)
            .FirstOrDefault();

        if (matched is not null)
        {
            var info = ServiceOperationPermissionInfo.FromRecord(matched.Rule);
            return IsDeny(matched.Rule)
                ? ServiceOperationAuthorizationResult.Deny(operationName, "matched-deny-rule", info)
                : ServiceOperationAuthorizationResult.Allow(operationName, "matched-allow-rule", info);
        }

        if (_permissionRoleAuthorizer is not null)
        {
            var allowedByPermissionMap = await _permissionRoleAuthorizer.AuthorizeAsync(
                request.TenantId,
                request.Principal,
                [operationName],
                [],
                ct).ConfigureAwait(false);

            if (allowedByPermissionMap)
            {
                return ServiceOperationAuthorizationResult.Allow(operationName, "matched-permission-role-map");
            }
        }

        return string.Equals(options.DefaultMode, ServiceOperationAuthorizationDefaultModes.Allow, StringComparison.OrdinalIgnoreCase)
            ? ServiceOperationAuthorizationResult.Allow(operationName, "default-allow")
            : ServiceOperationAuthorizationResult.Deny(operationName, "default-require-permission");
    }

    private static bool TenantMatches(ServiceOperationPermissionRule rule, Guid tenantId)
        => rule.TenantId is null || rule.TenantId == Guid.Empty || rule.TenantId == tenantId;

    private static bool SubjectMatches(ServiceOperationPermissionRule rule, string subjectType)
        => rule.SubjectTypes.Count == 0 ||
           rule.SubjectTypes.Any(x => string.Equals(x, subjectType, StringComparison.OrdinalIgnoreCase));

    private static bool RoleMatches(
        ServiceOperationPermissionRule rule,
        IReadOnlySet<string> roleNames,
        IReadOnlySet<Guid> roleIds)
    {
        if (rule.RoleNames.Count == 0 && rule.RoleIds.Count == 0)
        {
            return true;
        }

        return rule.RoleNames.Any(roleNames.Contains) ||
               rule.RoleIds.Any(roleIds.Contains);
    }

    private static int PatternSpecificity(string pattern, string operationName)
    {
        if (string.Equals(pattern, "*", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.Equals(pattern, operationName, StringComparison.OrdinalIgnoreCase))
        {
            return 100_000 + pattern.Length;
        }

        if (pattern.EndsWith(".*", StringComparison.Ordinal))
        {
            var prefix = pattern[..^1];
            return operationName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? prefix.Length
                : -1;
        }

        if (pattern.EndsWith("*", StringComparison.Ordinal))
        {
            var prefix = pattern[..^1];
            return operationName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? prefix.Length
                : -1;
        }

        return -1;
    }

    private static int SourcePrecedence(string source)
        => source switch
        {
            ServiceOperationPermissionSources.EmergencyConfiguration => 300,
            ServiceOperationPermissionSources.Configuration => 200,
            ServiceOperationPermissionSources.Store => 100,
            _ => 0
        };

    private static bool IsDeny(ServiceOperationPermissionRule rule)
        => string.Equals(rule.Effect, ServiceOperationPermissionEffects.Deny, StringComparison.OrdinalIgnoreCase);

    private sealed record RuleMatch(ServiceOperationPermissionRule Rule, int Specificity);

    private sealed record ServiceOperationSubjectContext(
        string SubjectType,
        IReadOnlySet<string> RoleNames,
        IReadOnlySet<Guid> RoleIds)
    {
        public static ServiceOperationSubjectContext FromPrincipal(ClaimsPrincipal principal)
            => new(
                ResolveSubjectType(principal),
                GetRoleNames(principal),
                GetRoleIds(principal));

        private static string ResolveSubjectType(ClaimsPrincipal principal)
        {
            var explicitType = FindFirstValue(principal, "subject_type") ??
                               FindFirstValue(principal, "sub_type") ??
                               FindFirstValue(principal, "api_subject_type");

            if (!string.IsNullOrWhiteSpace(explicitType))
            {
                if (string.Equals(explicitType, "credential", StringComparison.OrdinalIgnoreCase))
                {
                    return AccessSubjectTypes.ApiCredential;
                }

                return explicitType.Trim();
            }

            if (principal.HasClaim(x =>
                    string.Equals(x.Type, "agent_id", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(x.Type, "agent_key", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(x.Type, "allowed_agent_key", StringComparison.OrdinalIgnoreCase)))
            {
                return AccessSubjectTypes.Agent;
            }

            if (principal.HasClaim(x => string.Equals(x.Type, "api_credential_id", StringComparison.OrdinalIgnoreCase)))
            {
                return AccessSubjectTypes.ApiCredential;
            }

            return AccessSubjectTypes.User;
        }

        private static string? FindFirstValue(ClaimsPrincipal principal, string claimType)
            => principal.Claims.FirstOrDefault(x => string.Equals(x.Type, claimType, StringComparison.OrdinalIgnoreCase))?.Value;

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
}
