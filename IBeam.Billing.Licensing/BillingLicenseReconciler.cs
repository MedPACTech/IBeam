using IBeam.Services.Abstractions;
using IBeam.Licensing;
using Microsoft.Extensions.Options;

namespace IBeam.Billing.Licensing;

[IBeamOperation("billing.licensing")]
public sealed class BillingLicenseReconciler : IBillingLicenseReconciler
{
    private readonly ITenantLicenseService _licenses;
    private readonly IOptions<BillingLicenseReconciliationOptions> _options;
    private readonly IServiceOperationExecutor _operations;

    public BillingLicenseReconciler(
        ITenantLicenseService licenses,
        IOptions<BillingLicenseReconciliationOptions>? options = null,
        IServiceOperationExecutor? operations = null)
    {
        _licenses = licenses;
        _options = options ?? Options.Create(new BillingLicenseReconciliationOptions());
        _operations = operations ?? new ServiceOperationExecutor();
    }

    [IBeamOperation("billing.licensing.reconcile")]
    public async Task<BillingLicenseReconciliationResult> ReconcileAsync(
        Guid tenantId,
        ReconcileBillingLicenseRequest request,
        CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => ReconcileCoreAsync(tenantId, request, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = request?.Subscription?.BillingSubscriptionId },
            ct).ConfigureAwait(false);

    private async Task<BillingLicenseReconciliationResult> ReconcileCoreAsync(
        Guid tenantId,
        ReconcileBillingLicenseRequest request,
        CancellationToken ct)
    {
        if (tenantId == Guid.Empty)
            throw new BillingException("tenantId is required.");
        if (request?.Subscription is null)
            throw new ArgumentNullException(nameof(request));

        var subscription = request.Subscription;
        if (subscription.TenantId != tenantId)
            throw new BillingException("Subscription tenant does not match reconciliation tenant.");

        if (IsCancellation(request, subscription))
            return await ApplyCancellationAsync(tenantId, request, subscription, ct).ConfigureAwait(false);

        if (IsPaymentFailure(request, subscription))
            return await ApplyPaymentFailureAsync(tenantId, request, subscription, ct).ConfigureAwait(false);

        if (IsPaymentSuccess(request, subscription) || IsManualGrant(subscription))
            return await CreateOrRenewAsync(tenantId, request, subscription, ct).ConfigureAwait(false);

        return new BillingLicenseReconciliationResult(
            BillingLicenseReconciliationActions.NoOp,
            null,
            $"Billing subscription status '{subscription.Status}' did not require a license change.");
    }

    private async Task<BillingLicenseReconciliationResult> CreateOrRenewAsync(
        Guid tenantId,
        ReconcileBillingLicenseRequest request,
        BillingSubscriptionInfo subscription,
        CancellationToken ct)
    {
        var plan = ResolvePlan(request, subscription)
            ?? throw new BillingException("Unable to map billing subscription to a license plan.");
        var existing = await FindExistingLicenseAsync(tenantId, subscription, ct).ConfigureAwait(false);
        var now = request.EffectiveUtc ?? DateTimeOffset.UtcNow;
        var starts = subscription.CurrentPeriodStartsUtc ?? now;
        var expires = subscription.CurrentPeriodEndsUtc ?? now.AddDays(request.RenewalPeriodDays ?? _options.Value.DefaultRenewalPeriodDays);
        var metadata = BuildMetadata(subscription, plan, request.Metadata);

        if (existing is null)
        {
            var created = await _licenses.GrantLicenseAsync(
                tenantId,
                new GrantTenantLicenseRequest
                {
                    PlanKey = plan.PlanKey,
                    Status = ResolveRuntimeStatus(subscription),
                    CommercialStatus = ResolveCommercialStatus(subscription),
                    SeatLimit = plan.SeatLimit ?? subscription.SeatQuantity,
                    Entitlements = plan.Entitlements,
                    StartsUtc = starts,
                    ExpiresUtc = expires,
                    ProviderName = subscription.ProviderName ?? subscription.Price?.ProviderName,
                    ProviderSubscriptionId = subscription.ProviderSubscriptionId,
                    ProviderPriceId = subscription.Price?.PriceId,
                    ProviderStatus = subscription.ProviderStatus ?? subscription.Status,
                    Metadata = metadata
                },
                subscription.UserId,
                ct).ConfigureAwait(false);

            return new BillingLicenseReconciliationResult(BillingLicenseReconciliationActions.Created, created, null);
        }

        var renewed = await _licenses.UpdateLicenseAsync(
            tenantId,
            existing.LicenseId,
            new UpdateTenantLicenseRequest
            {
                Status = ResolveRuntimeStatus(subscription),
                CommercialStatus = ResolveCommercialStatus(subscription),
                SeatLimit = plan.SeatLimit ?? subscription.SeatQuantity ?? existing.SeatLimit,
                StartsUtc = existing.StartsUtc,
                ExpiresUtc = existing.ExpiresUtc is { } current && current > expires ? current : expires,
                ProviderName = subscription.ProviderName ?? subscription.Price?.ProviderName,
                ProviderSubscriptionId = subscription.ProviderSubscriptionId,
                ProviderPriceId = subscription.Price?.PriceId,
                ProviderStatus = subscription.ProviderStatus ?? subscription.Status,
                Metadata = metadata
            },
            ct).ConfigureAwait(false);

        return new BillingLicenseReconciliationResult(BillingLicenseReconciliationActions.Renewed, renewed, null);
    }

    private async Task<BillingLicenseReconciliationResult> ApplyCancellationAsync(
        Guid tenantId,
        ReconcileBillingLicenseRequest request,
        BillingSubscriptionInfo subscription,
        CancellationToken ct)
    {
        var existing = await FindExistingLicenseAsync(tenantId, subscription, ct).ConfigureAwait(false);
        if (existing is null)
            return new BillingLicenseReconciliationResult(BillingLicenseReconciliationActions.NoOp, null, "No matching license was found.");

        var behavior = NormalizeBehavior(request.CancellationBehavior, _options.Value.CancellationBehavior);
        if (behavior == BillingLicenseCancellationBehaviors.Revoke)
        {
            await _licenses.RevokeLicenseAsync(tenantId, existing.LicenseId, "Billing subscription canceled.", ct).ConfigureAwait(false);
            var revoked = (await _licenses.ListTenantLicensesAsync(tenantId, ct).ConfigureAwait(false))
                .FirstOrDefault(x => x.LicenseId == existing.LicenseId);
            return new BillingLicenseReconciliationResult(BillingLicenseReconciliationActions.Revoked, revoked, null);
        }

        var metadata = new Dictionary<string, string>(existing.Metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["billingCancellationUtc"] = (request.EffectiveUtc ?? DateTimeOffset.UtcNow).ToString("O")
        };

        var status = behavior == BillingLicenseCancellationBehaviors.Expire
            ? LicenseStatuses.Expired
            : existing.Status;
        var action = behavior == BillingLicenseCancellationBehaviors.Expire
            ? BillingLicenseReconciliationActions.Expired
            : BillingLicenseReconciliationActions.Suspended;

        if (behavior == BillingLicenseCancellationBehaviors.ScheduleRevocation)
        {
            metadata["billingScheduledRevocationUtc"] = (subscription.CurrentPeriodEndsUtc ?? request.EffectiveUtc ?? DateTimeOffset.UtcNow).ToString("O");
            status = existing.Status;
            action = BillingLicenseReconciliationActions.ScheduledRevocation;
        }
        else if (behavior == BillingLicenseCancellationBehaviors.Suspend)
        {
            status = LicenseStatuses.Suspended;
        }

        var updated = await _licenses.UpdateLicenseAsync(
            tenantId,
            existing.LicenseId,
            new UpdateTenantLicenseRequest
            {
                Status = status,
                CommercialStatus = LicenseCommercialStatuses.Canceled,
                ExpiresUtc = behavior == BillingLicenseCancellationBehaviors.Expire
                    ? request.EffectiveUtc ?? DateTimeOffset.UtcNow
                    : subscription.CurrentPeriodEndsUtc ?? existing.ExpiresUtc,
                Metadata = metadata
            },
            ct).ConfigureAwait(false);

        return new BillingLicenseReconciliationResult(action, updated, null);
    }

    private async Task<BillingLicenseReconciliationResult> ApplyPaymentFailureAsync(
        Guid tenantId,
        ReconcileBillingLicenseRequest request,
        BillingSubscriptionInfo subscription,
        CancellationToken ct)
    {
        var existing = await FindExistingLicenseAsync(tenantId, subscription, ct).ConfigureAwait(false);
        if (existing is null)
            return new BillingLicenseReconciliationResult(BillingLicenseReconciliationActions.NoOp, null, "No matching license was found.");

        var behavior = NormalizeBehavior(request.PaymentFailureBehavior, _options.Value.PaymentFailureBehavior);
        if (behavior == BillingLicensePaymentFailureBehaviors.NoOp)
            return new BillingLicenseReconciliationResult(BillingLicenseReconciliationActions.NoOp, existing, null);

        var updated = await _licenses.UpdateLicenseAsync(
            tenantId,
            existing.LicenseId,
            new UpdateTenantLicenseRequest
            {
                Status = behavior == BillingLicensePaymentFailureBehaviors.Expire ? LicenseStatuses.Expired : LicenseStatuses.Suspended,
                CommercialStatus = LicenseCommercialStatuses.PastDue,
                ProviderStatus = subscription.ProviderStatus ?? subscription.Status,
                ExpiresUtc = behavior == BillingLicensePaymentFailureBehaviors.Expire
                    ? request.EffectiveUtc ?? DateTimeOffset.UtcNow
                    : existing.ExpiresUtc
            },
            ct).ConfigureAwait(false);

        return new BillingLicenseReconciliationResult(
            behavior == BillingLicensePaymentFailureBehaviors.Expire
                ? BillingLicenseReconciliationActions.Expired
                : BillingLicenseReconciliationActions.Suspended,
            updated,
            null);
    }

    private async Task<TenantLicenseInfo?> FindExistingLicenseAsync(Guid tenantId, BillingSubscriptionInfo subscription, CancellationToken ct)
    {
        var licenses = await _licenses.ListTenantLicensesAsync(tenantId, ct).ConfigureAwait(false);
        return licenses.FirstOrDefault(x =>
            !string.IsNullOrWhiteSpace(subscription.ProviderSubscriptionId) &&
            string.Equals(x.ProviderSubscriptionId, subscription.ProviderSubscriptionId, StringComparison.OrdinalIgnoreCase))
            ?? licenses.FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(subscription.Price?.PriceId) &&
                string.Equals(x.ProviderPriceId, subscription.Price.PriceId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.PlanKey, ResolvePlanKey(subscription), StringComparison.OrdinalIgnoreCase));
    }

    private BillingPricePlanMappingOptions? ResolvePlan(ReconcileBillingLicenseRequest request, BillingSubscriptionInfo subscription)
    {
        var explicitPlanKey = BillingPriceReferenceInfo.NormalizeOptional(request.PlanKey)
                              ?? ResolvePlanKey(subscription);
        var mapping = _options.Value.PriceMappings.FirstOrDefault(x =>
            string.Equals(BillingPriceReferenceInfo.NormalizeOptional(x.ProviderName), subscription.Price?.ProviderName ?? subscription.ProviderName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(BillingPriceReferenceInfo.NormalizeOptional(x.PriceId), subscription.Price?.PriceId, StringComparison.OrdinalIgnoreCase));

        if (mapping is not null)
            return mapping;

        return explicitPlanKey is null
            ? null
            : new BillingPricePlanMappingOptions { PlanKey = explicitPlanKey };
    }

    private static string? ResolvePlanKey(BillingSubscriptionInfo subscription)
        => BillingPriceReferenceInfo.NormalizeOptional(subscription.PlanKey)
           ?? BillingPriceReferenceInfo.NormalizeOptional(subscription.Price?.PlanKey);

    private static bool IsPaymentSuccess(ReconcileBillingLicenseRequest request, BillingSubscriptionInfo subscription)
        => IsAny(request.EventType, "invoice.paid", "payment.succeeded", "checkout.session.completed", "customer.subscription.created", "customer.subscription.updated") ||
           IsAny(subscription.Status, BillingSubscriptionStatuses.Active, BillingSubscriptionStatuses.Trialing, BillingSubscriptionStatuses.Manual);

    private static bool IsManualGrant(BillingSubscriptionInfo subscription)
        => IsAny(subscription.BillingMode, BillingModes.ManualInvoice, BillingModes.SupportManaged, BillingModes.AnnualContract) &&
           IsAny(subscription.Status, BillingSubscriptionStatuses.Active, BillingSubscriptionStatuses.Manual);

    private static bool IsCancellation(ReconcileBillingLicenseRequest request, BillingSubscriptionInfo subscription)
        => IsAny(request.EventType, "customer.subscription.deleted", "subscription.canceled", "subscription.cancelled") ||
           IsAny(subscription.Status, BillingSubscriptionStatuses.Canceled);

    private static bool IsPaymentFailure(ReconcileBillingLicenseRequest request, BillingSubscriptionInfo subscription)
        => IsAny(request.EventType, "invoice.payment_failed", "payment.failed") ||
           IsAny(subscription.Status, BillingSubscriptionStatuses.PastDue, BillingSubscriptionStatuses.Incomplete);

    private static string ResolveRuntimeStatus(BillingSubscriptionInfo subscription)
        => IsAny(subscription.BillingMode, BillingModes.ManualInvoice, BillingModes.SupportManaged)
            ? LicenseStatuses.Manual
            : LicenseStatuses.Active;

    private static string ResolveCommercialStatus(BillingSubscriptionInfo subscription)
        => IsAny(subscription.BillingMode, BillingModes.ManualInvoice, BillingModes.SupportManaged)
            ? LicenseCommercialStatuses.Manual
            : LicenseCommercialStatuses.Paid;

    private static Dictionary<string, string> BuildMetadata(
        BillingSubscriptionInfo subscription,
        BillingPricePlanMappingOptions plan,
        IReadOnlyDictionary<string, string>? requestMetadata)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["billingCustomerId"] = subscription.BillingCustomerId.ToString(),
            ["billingSubscriptionId"] = subscription.BillingSubscriptionId.ToString(),
            ["billingMode"] = subscription.BillingMode
        };

        foreach (var item in BillingPriceReferenceInfo.NormalizeMetadata(plan.Metadata))
            metadata[item.Key] = item.Value;
        foreach (var item in BillingPriceReferenceInfo.NormalizeMetadata(requestMetadata))
            metadata[item.Key] = item.Value;

        return metadata;
    }

    private static bool IsAny(string? value, params string[] values)
    {
        var normalized = BillingModes.NormalizeKnown(value, string.Empty);
        return values.Any(x => string.Equals(normalized, BillingModes.NormalizeKnown(x, string.Empty), StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeBehavior(string? value, string defaultValue)
        => BillingModes.NormalizeKnown(value, defaultValue);
}
