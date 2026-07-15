namespace IBeam.AccessControl.Services;

public sealed class StoreServiceOperationPermissionRuleProvider : IServiceOperationPermissionRuleProvider
{
    private readonly IServiceOperationPermissionStore _store;

    public StoreServiceOperationPermissionRuleProvider(IServiceOperationPermissionStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public Task<IReadOnlyList<ServiceOperationPermissionRule>> ListRulesAsync(Guid tenantId, CancellationToken ct = default)
        => _store.ListRulesAsync(tenantId, ct);
}
