using IBeam.Licensing;
using IBeam.Services.Abstractions;

namespace IBeam.Billing.Licensing;

[IBeamOperation("billing.provider-migration")]
public sealed class BillingProviderMigrationService : IBillingProviderMigrationService
{
    private readonly IBillingSubscriptionService _subscriptions;
    private readonly IBillingSubscriptionProviderBindingStore _bindings;
    private readonly ITenantLicenseService _licenses;
    private readonly IBillingLicenseReconciler _reconciler;
    private readonly IServiceOperationExecutor _operations;

    public BillingProviderMigrationService(
        IBillingSubscriptionService subscriptions,
        IBillingSubscriptionProviderBindingStore bindings,
        ITenantLicenseService licenses,
        IBillingLicenseReconciler reconciler,
        IServiceOperationExecutor? operations = null)
    {
        _subscriptions = subscriptions;
        _bindings = bindings;
        _licenses = licenses;
        _reconciler = reconciler;
        _operations = operations ?? new ServiceOperationExecutor();
    }

    [IBeamOperation("billing.provider-migration.migrate")]
    public async Task<BillingProviderMigrationInfo> MigrateAsync(
        Guid tenantId,
        MigrateBillingProviderRequest request,
        CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => MigrateCoreAsync(tenantId, request, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = request?.BillingSubscriptionId },
            ct).ConfigureAwait(false);

    private async Task<BillingProviderMigrationInfo> MigrateCoreAsync(
        Guid tenantId,
        MigrateBillingProviderRequest request,
        CancellationToken ct)
    {
        if (tenantId == Guid.Empty)
            throw new BillingException("tenantId is required.");
        ArgumentNullException.ThrowIfNull(request);
        if (request.BillingSubscriptionId == Guid.Empty)
            throw new BillingException("billingSubscriptionId is required.");

        var targetProvider = BillingPriceReferenceInfo.NormalizeRequired(request.TargetProviderName, nameof(request.TargetProviderName)).ToLowerInvariant();
        var targetSubscriptionId = BillingPriceReferenceInfo.NormalizeRequired(request.TargetProviderSubscriptionId, nameof(request.TargetProviderSubscriptionId));
        var idempotencyKey = BillingPriceReferenceInfo.NormalizeRequired(request.IdempotencyKey, nameof(request.IdempotencyKey));
        var targetPrice = request.TargetPrice ?? throw new BillingException("TargetPrice is required for provider migration.");
        if (!string.Equals(targetPrice.ProviderName, targetProvider, StringComparison.OrdinalIgnoreCase))
            throw new BillingException("Target price provider must match the target provider.");
        var targetStatus = BillingSubscriptionStatuses.Normalize(request.TargetSubscriptionStatus);
        if (targetStatus is not (BillingSubscriptionStatuses.Active or BillingSubscriptionStatuses.Trialing or BillingSubscriptionStatuses.Manual))
            throw new BillingException("The target subscription must be active, trialing, or manual before migration.");

        var replay = await _bindings.GetMigrationAsync(
            tenantId,
            request.BillingSubscriptionId,
            targetProvider,
            idempotencyKey,
            ct).ConfigureAwait(false);
        if (replay is not null)
            return await BuildResultAsync(replay, true, ct).ConfigureAwait(false);

        var subscription = await _subscriptions.GetSubscriptionAsync(tenantId, request.BillingSubscriptionId, ct).ConfigureAwait(false)
            ?? throw new BillingException($"Billing subscription '{request.BillingSubscriptionId}' was not found.");
        var sourceProvider = BillingPriceReferenceInfo.NormalizeRequired(subscription.ProviderName ?? string.Empty, "sourceProviderName").ToLowerInvariant();
        var sourceSubscriptionId = BillingPriceReferenceInfo.NormalizeRequired(subscription.ProviderSubscriptionId ?? string.Empty, "sourceProviderSubscriptionId");
        if (string.Equals(sourceProvider, targetProvider, StringComparison.OrdinalIgnoreCase))
            throw new BillingException("Source and target billing providers must be different.");

        var license = (await _licenses.ListTenantLicensesAsync(tenantId, ct).ConfigureAwait(false))
            .SingleOrDefault(x =>
                string.Equals(x.ProviderName, sourceProvider, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.ProviderSubscriptionId, sourceSubscriptionId, StringComparison.OrdinalIgnoreCase))
            ?? throw new BillingException("No license is bound to the source billing subscription.");

        var targetSnapshot = subscription with
        {
            Status = targetStatus,
            ProviderName = targetProvider,
            ProviderSubscriptionId = targetSubscriptionId,
            ProviderStatus = BillingPriceReferenceInfo.NormalizeOptional(request.TargetProviderStatus) ?? BillingSubscriptionStatuses.Active,
            Price = targetPrice,
            SeatQuantity = request.SeatQuantity ?? subscription.SeatQuantity,
            CurrentPeriodStartsUtc = request.CurrentPeriodStartsUtc ?? subscription.CurrentPeriodStartsUtc,
            CurrentPeriodEndsUtc = request.CurrentPeriodEndsUtc ?? subscription.CurrentPeriodEndsUtc,
            UpdatedUtc = DateTimeOffset.UtcNow,
            Metadata = MergeMetadata(subscription.Metadata, request.Metadata)
        };

        await _reconciler.ReconcileAsync(
            tenantId,
            new ReconcileBillingLicenseRequest
            {
                Subscription = targetSnapshot,
                EventType = "customer.subscription.updated",
                LicenseKey = license.LicenseKey,
                ProviderCustomerId = request.TargetProviderCustomerId,
                Metadata = new Dictionary<string, string>
                {
                    ["billingProviderMigration"] = "true",
                    ["billingPreviousProvider"] = sourceProvider
                }
            },
            ct).ConfigureAwait(false);

        var updatedSubscription = await _subscriptions.UpdateSubscriptionAsync(
            tenantId,
            subscription.BillingSubscriptionId,
            new UpdateBillingSubscriptionRequest
            {
                ProviderName = targetProvider,
                ProviderSubscriptionId = targetSubscriptionId,
                ProviderStatus = targetSnapshot.ProviderStatus,
                Status = targetSnapshot.Status,
                Price = targetPrice,
                SeatQuantity = targetSnapshot.SeatQuantity,
                CurrentPeriodStartsUtc = targetSnapshot.CurrentPeriodStartsUtc,
                CurrentPeriodEndsUtc = targetSnapshot.CurrentPeriodEndsUtc,
                Metadata = new Dictionary<string, string>(targetSnapshot.Metadata, StringComparer.OrdinalIgnoreCase)
            },
            ct).ConfigureAwait(false);

        var completedUtc = DateTimeOffset.UtcNow;
        var sourceBinding = new BillingSubscriptionProviderBindingInfo(
            Guid.NewGuid(), tenantId, subscription.BillingSubscriptionId, sourceProvider,
            license.ProviderCustomerId, sourceSubscriptionId, subscription.Price?.PriceId,
            false, subscription.CreatedUtc, completedUtc, subscription.Metadata);
        var targetBinding = new BillingSubscriptionProviderBindingInfo(
            Guid.NewGuid(), tenantId, subscription.BillingSubscriptionId, targetProvider,
            BillingPriceReferenceInfo.NormalizeOptional(request.TargetProviderCustomerId), targetSubscriptionId, targetPrice.PriceId,
            true, completedUtc, null, BillingPriceReferenceInfo.NormalizeMetadata(request.Metadata));
        var migration = new BillingProviderMigrationRecord(
            Guid.NewGuid(), tenantId, subscription.BillingSubscriptionId, idempotencyKey,
            sourceProvider, targetProvider, license.LicenseKey, targetBinding.BindingId, completedUtc);

        var committed = await _bindings.CommitMigrationAsync(migration, sourceBinding, targetBinding, ct).ConfigureAwait(false);
        return new BillingProviderMigrationInfo(
            committed.MigrationId,
            updatedSubscription,
            committed.LicenseKey,
            await _bindings.ListBindingsAsync(tenantId, subscription.BillingSubscriptionId, ct).ConfigureAwait(false),
            false);
    }

    private async Task<BillingProviderMigrationInfo> BuildResultAsync(
        BillingProviderMigrationRecord migration,
        bool wasReplay,
        CancellationToken ct)
    {
        var subscription = await _subscriptions.GetSubscriptionAsync(migration.TenantId, migration.BillingSubscriptionId, ct).ConfigureAwait(false)
            ?? throw new BillingException($"Billing subscription '{migration.BillingSubscriptionId}' was not found.");
        return new BillingProviderMigrationInfo(
            migration.MigrationId,
            subscription,
            migration.LicenseKey,
            await _bindings.ListBindingsAsync(migration.TenantId, migration.BillingSubscriptionId, ct).ConfigureAwait(false),
            wasReplay);
    }

    private static IReadOnlyDictionary<string, string> MergeMetadata(
        IReadOnlyDictionary<string, string> current,
        IReadOnlyDictionary<string, string>? changes)
    {
        var merged = new Dictionary<string, string>(current, StringComparer.OrdinalIgnoreCase);
        foreach (var item in BillingPriceReferenceInfo.NormalizeMetadata(changes))
            merged[item.Key] = item.Value;
        return merged;
    }
}
