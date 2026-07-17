using IBeam.Services.Abstractions;

namespace IBeam.AccessControl.Services;

[IBeamOperation("accesscontrol.serviceoperations")]
public sealed class ServiceOperationPermissionService : IServiceOperationPermissionService
{
    private readonly IServiceOperationPermissionStore _store;
    private readonly IServiceOperationExecutor _operations;

    public ServiceOperationPermissionService(IServiceOperationPermissionStore store, IServiceOperationExecutor? operations = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _operations = operations ?? new ServiceOperationExecutor();
    }

    [IBeamOperation("accesscontrol.serviceoperations.list")]
    public async Task<IReadOnlyList<ServiceOperationPermissionInfo>> ListRulesAsync(Guid tenantId, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => ListRulesCoreAsync(tenantId, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId },
            ct).ConfigureAwait(false);

    private async Task<IReadOnlyList<ServiceOperationPermissionInfo>> ListRulesCoreAsync(Guid tenantId, CancellationToken ct)
    {
        ValidateTenantId(tenantId);
        var records = await _store.ListRulesAsync(tenantId, ct).ConfigureAwait(false);
        return records.Select(ServiceOperationPermissionInfo.FromRecord).ToList();
    }

    [IBeamOperation("accesscontrol.serviceoperations.upsert")]
    public async Task<ServiceOperationPermissionInfo> UpsertRuleAsync(
        Guid tenantId,
        UpsertServiceOperationPermissionRequest request,
        Guid? updatedByUserId = null,
        CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => UpsertRuleCoreAsync(tenantId, request, updatedByUserId, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = request?.RuleId },
            ct).ConfigureAwait(false);

    private async Task<ServiceOperationPermissionInfo> UpsertRuleCoreAsync(
        Guid tenantId,
        UpsertServiceOperationPermissionRequest request,
        Guid? updatedByUserId,
        CancellationToken ct)
    {
        ValidateTenantId(tenantId);
        ArgumentNullException.ThrowIfNull(request);
        var now = DateTimeOffset.UtcNow;
        var existing = request.RuleId is Guid ruleId && ruleId != Guid.Empty
            ? await _store.GetRuleAsync(tenantId, ruleId, ct).ConfigureAwait(false)
            : null;

        var record = new ServiceOperationPermissionRule(
            RuleId: request.RuleId is Guid id && id != Guid.Empty ? id : Guid.NewGuid(),
            TenantId: tenantId,
            Pattern: NormalizePattern(request.Pattern),
            Effect: NormalizeEffect(request.Effect),
            SubjectTypes: NormalizeSubjectTypes(request.SubjectTypes),
            RoleNames: NormalizeRoleNames(request.RoleNames),
            RoleIds: NormalizeRoleIds(request.RoleIds),
            Priority: request.Priority,
            Source: ServiceOperationPermissionSources.Store,
            Status: ServiceOperationPermissionStatuses.Active,
            CreatedUtc: existing?.CreatedUtc ?? now,
            UpdatedUtc: now,
            UpdatedByUserId: updatedByUserId);

        return ServiceOperationPermissionInfo.FromRecord(await _store.UpsertRuleAsync(record, ct).ConfigureAwait(false));
    }

    [IBeamOperation("accesscontrol.serviceoperations.disable")]
    public Task DisableRuleAsync(Guid tenantId, Guid ruleId, Guid? updatedByUserId = null, CancellationToken ct = default)
        => _operations.ExecuteAsync(
            this,
            token => DisableRuleCoreAsync(tenantId, ruleId, updatedByUserId, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = ruleId },
            ct);

    private Task DisableRuleCoreAsync(Guid tenantId, Guid ruleId, Guid? updatedByUserId, CancellationToken ct)
    {
        ValidateTenantId(tenantId);
        ValidateRuleId(ruleId);
        return _store.DisableRuleAsync(tenantId, ruleId, updatedByUserId, ct);
    }

    [IBeamOperation("accesscontrol.serviceoperations.delete")]
    public Task DeleteRuleAsync(Guid tenantId, Guid ruleId, CancellationToken ct = default)
        => _operations.ExecuteAsync(
            this,
            token => DeleteRuleCoreAsync(tenantId, ruleId, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = ruleId },
            ct);

    private Task DeleteRuleCoreAsync(Guid tenantId, Guid ruleId, CancellationToken ct)
    {
        ValidateTenantId(tenantId);
        ValidateRuleId(ruleId);
        return _store.DeleteRuleAsync(tenantId, ruleId, ct);
    }

    internal static void ValidateTenantId(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new AccessControlException("tenantId is required.");
    }

    internal static void ValidateRuleId(Guid ruleId)
    {
        if (ruleId == Guid.Empty)
            throw new AccessControlException("ruleId is required.");
    }

    internal static string NormalizePattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            throw new AccessControlException("pattern is required.");

        return pattern.Trim();
    }

    internal static string NormalizeEffect(string effect)
        => string.Equals(effect, ServiceOperationPermissionEffects.Deny, StringComparison.OrdinalIgnoreCase)
            ? ServiceOperationPermissionEffects.Deny
            : ServiceOperationPermissionEffects.Allow;

    internal static IReadOnlyList<string> NormalizeSubjectTypes(IReadOnlyList<string>? subjectTypes)
        => (subjectTypes ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

    internal static IReadOnlyList<string> NormalizeRoleNames(IReadOnlyList<string>? roleNames)
        => (roleNames ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

    internal static IReadOnlyList<Guid> NormalizeRoleIds(IReadOnlyList<Guid>? roleIds)
        => (roleIds ?? Array.Empty<Guid>())
            .Where(x => x != Guid.Empty)
            .Distinct()
            .OrderBy(x => x)
            .ToList();
}

