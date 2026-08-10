using System.Collections.Concurrent;

namespace IBeam.Billing.Services;

public sealed class InMemoryBillingSubscriptionProviderBindingStore : IBillingSubscriptionProviderBindingStore
{
    private readonly object _sync = new();
    private readonly ConcurrentDictionary<(Guid TenantId, Guid SubscriptionId, Guid BindingId), BillingSubscriptionProviderBindingInfo> _bindings = [];
    private readonly ConcurrentDictionary<string, BillingProviderMigrationRecord> _migrations = new(StringComparer.OrdinalIgnoreCase);

    public Task<BillingProviderMigrationRecord?> GetMigrationAsync(
        Guid tenantId,
        Guid billingSubscriptionId,
        string targetProviderName,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        _migrations.TryGetValue(MigrationKey(tenantId, billingSubscriptionId, targetProviderName, idempotencyKey), out var migration);
        return Task.FromResult(migration);
    }

    public Task<IReadOnlyList<BillingSubscriptionProviderBindingInfo>> ListBindingsAsync(
        Guid tenantId,
        Guid billingSubscriptionId,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<BillingSubscriptionProviderBindingInfo>>(
            _bindings.Values
                .Where(x => x.TenantId == tenantId && x.BillingSubscriptionId == billingSubscriptionId)
                .OrderBy(x => x.ActivatedUtc)
                .ToList());

    public Task<BillingProviderMigrationRecord> CommitMigrationAsync(
        BillingProviderMigrationRecord migration,
        BillingSubscriptionProviderBindingInfo sourceBinding,
        BillingSubscriptionProviderBindingInfo targetBinding,
        CancellationToken ct = default)
    {
        var key = MigrationKey(migration.TenantId, migration.BillingSubscriptionId, migration.TargetProviderName, migration.IdempotencyKey);
        lock (_sync)
        {
            if (_migrations.TryGetValue(key, out var existing))
                return Task.FromResult(existing);

            foreach (var bindingKey in _bindings.Keys.Where(x =>
                         x.TenantId == migration.TenantId &&
                         x.SubscriptionId == migration.BillingSubscriptionId))
            {
                if (_bindings.TryGetValue(bindingKey, out var binding) && binding.IsActive)
                    _bindings[bindingKey] = binding with { IsActive = false, RetiredUtc = migration.CompletedUtc };
            }

            var sourceAlreadyRecorded = _bindings.Values.Any(x =>
                x.TenantId == sourceBinding.TenantId &&
                x.BillingSubscriptionId == sourceBinding.BillingSubscriptionId &&
                string.Equals(x.ProviderName, sourceBinding.ProviderName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.ProviderSubscriptionId, sourceBinding.ProviderSubscriptionId, StringComparison.OrdinalIgnoreCase));
            if (!sourceAlreadyRecorded)
                _bindings[(sourceBinding.TenantId, sourceBinding.BillingSubscriptionId, sourceBinding.BindingId)] = sourceBinding;
            _bindings[(targetBinding.TenantId, targetBinding.BillingSubscriptionId, targetBinding.BindingId)] = targetBinding;
            _migrations[key] = migration;
            return Task.FromResult(migration);
        }
    }

    private static string MigrationKey(Guid tenantId, Guid billingSubscriptionId, string providerName, string idempotencyKey)
        => $"{tenantId:N}:{billingSubscriptionId:N}:{providerName.Trim().ToLowerInvariant()}:{idempotencyKey.Trim()}";
}
