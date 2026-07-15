using System.Collections.Concurrent;

namespace IBeam.AccessControl.Services;

public sealed class InMemoryServiceOperationPermissionStore : IServiceOperationPermissionStore
{
    private readonly ConcurrentDictionary<(Guid TenantId, Guid RuleId), ServiceOperationPermissionRule> _rules = [];

    public Task<IReadOnlyList<ServiceOperationPermissionRule>> ListRulesAsync(Guid tenantId, CancellationToken ct = default)
    {
        ServiceOperationPermissionService.ValidateTenantId(tenantId);
        return Task.FromResult<IReadOnlyList<ServiceOperationPermissionRule>>(
            _rules.Values
                .Where(x => x.TenantId == tenantId)
                .OrderByDescending(x => x.Priority)
                .ThenBy(x => x.Pattern, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    public Task<ServiceOperationPermissionRule?> GetRuleAsync(Guid tenantId, Guid ruleId, CancellationToken ct = default)
    {
        ServiceOperationPermissionService.ValidateTenantId(tenantId);
        ServiceOperationPermissionService.ValidateRuleId(ruleId);
        _rules.TryGetValue((tenantId, ruleId), out var rule);
        return Task.FromResult(rule);
    }

    public Task<ServiceOperationPermissionRule> UpsertRuleAsync(ServiceOperationPermissionRule rule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (rule.TenantId is not Guid tenantId)
        {
            throw new AccessControlException("tenantId is required.");
        }

        ServiceOperationPermissionService.ValidateTenantId(tenantId);
        ServiceOperationPermissionService.ValidateRuleId(rule.RuleId);
        _rules[(tenantId, rule.RuleId)] = rule;
        return Task.FromResult(rule);
    }

    public Task DisableRuleAsync(Guid tenantId, Guid ruleId, Guid? updatedByUserId = null, CancellationToken ct = default)
    {
        ServiceOperationPermissionService.ValidateTenantId(tenantId);
        ServiceOperationPermissionService.ValidateRuleId(ruleId);
        _rules.AddOrUpdate(
            (tenantId, ruleId),
            _ => throw new AccessControlException($"Service operation permission rule '{ruleId}' was not found."),
            (_, existing) => existing with
            {
                Status = ServiceOperationPermissionStatuses.Disabled,
                UpdatedUtc = DateTimeOffset.UtcNow,
                UpdatedByUserId = updatedByUserId
            });

        return Task.CompletedTask;
    }

    public Task DeleteRuleAsync(Guid tenantId, Guid ruleId, CancellationToken ct = default)
    {
        ServiceOperationPermissionService.ValidateTenantId(tenantId);
        ServiceOperationPermissionService.ValidateRuleId(ruleId);
        _rules.TryRemove((tenantId, ruleId), out _);
        return Task.CompletedTask;
    }
}

