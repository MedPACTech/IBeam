namespace IBeam.AccessControl.Services;

public sealed class ServiceOperationPermissionService : IServiceOperationPermissionService
{
    private readonly IServiceOperationPermissionStore _store;

    public ServiceOperationPermissionService(IServiceOperationPermissionStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<IReadOnlyList<ServiceOperationPermissionInfo>> ListRulesAsync(Guid tenantId, CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        var records = await _store.ListRulesAsync(tenantId, ct).ConfigureAwait(false);
        return records.Select(ServiceOperationPermissionInfo.FromRecord).ToList();
    }

    public async Task<ServiceOperationPermissionInfo> UpsertRuleAsync(
        Guid tenantId,
        UpsertServiceOperationPermissionRequest request,
        Guid? updatedByUserId = null,
        CancellationToken ct = default)
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

    public Task DisableRuleAsync(Guid tenantId, Guid ruleId, Guid? updatedByUserId = null, CancellationToken ct = default)
    {
        ValidateTenantId(tenantId);
        ValidateRuleId(ruleId);
        return _store.DisableRuleAsync(tenantId, ruleId, updatedByUserId, ct);
    }

    public Task DeleteRuleAsync(Guid tenantId, Guid ruleId, CancellationToken ct = default)
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

