using Microsoft.Extensions.Options;

namespace IBeam.AccessControl.Services;

public sealed class ConfigServiceOperationPermissionRuleProvider : IServiceOperationPermissionRuleProvider
{
    private readonly IOptionsMonitor<ServiceOperationAuthorizationOptions> _options;

    public ConfigServiceOperationPermissionRuleProvider(IOptionsMonitor<ServiceOperationAuthorizationOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public Task<IReadOnlyList<ServiceOperationPermissionRule>> ListRulesAsync(Guid tenantId, CancellationToken ct = default)
    {
        ServiceOperationPermissionService.ValidateTenantId(tenantId);
        var options = _options.CurrentValue;
        var rules = new List<ServiceOperationPermissionRule>();
        rules.AddRange(ToRules(tenantId, options.EmergencyOverrides, ServiceOperationPermissionSources.EmergencyConfiguration));
        rules.AddRange(ToRules(tenantId, options.Rules, ServiceOperationPermissionSources.Configuration));
        return Task.FromResult<IReadOnlyList<ServiceOperationPermissionRule>>(rules);
    }

    private static IEnumerable<ServiceOperationPermissionRule> ToRules(
        Guid tenantId,
        IReadOnlyList<ServiceOperationPermissionRuleOptions> options,
        string source)
    {
        for (var i = 0; i < options.Count; i++)
        {
            var rule = options[i];
            if (rule.TenantId is Guid configuredTenant && configuredTenant != Guid.Empty && configuredTenant != tenantId)
            {
                continue;
            }

            yield return new ServiceOperationPermissionRule(
                RuleId: StableRuleId(source, i, rule),
                TenantId: rule.TenantId,
                Pattern: ServiceOperationPermissionService.NormalizePattern(rule.Pattern),
                Effect: ServiceOperationPermissionService.NormalizeEffect(rule.Effect),
                SubjectTypes: ServiceOperationPermissionService.NormalizeSubjectTypes(rule.SubjectTypes),
                RoleNames: ServiceOperationPermissionService.NormalizeRoleNames(rule.RoleNames),
                RoleIds: ServiceOperationPermissionService.NormalizeRoleIds(rule.RoleIds),
                Priority: rule.Priority,
                Source: source,
                Status: ServiceOperationPermissionStatuses.Active,
                CreatedUtc: DateTimeOffset.MinValue,
                UpdatedUtc: DateTimeOffset.MinValue,
                UpdatedByUserId: null);
        }
    }

    private static Guid StableRuleId(string source, int index, ServiceOperationPermissionRuleOptions rule)
        => System.Security.Cryptography.MD5.HashData(
                System.Text.Encoding.UTF8.GetBytes($"{source}|{index}|{rule.TenantId}|{rule.Pattern}|{rule.Effect}"))
            is var bytes
            ? new Guid(bytes)
            : Guid.NewGuid();
}

