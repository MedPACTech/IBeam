using IBeam.Services.Abstractions;
using IBeam.Licensing;
using Microsoft.Extensions.Options;

namespace IBeam.Billing.Licensing;

[IBeamOperation("billing.licensing")]
public sealed class BillingLicenseReconciler : IBillingLicenseReconciler
{
    private readonly ITenantLicenseService _licenses;
    private readonly ILicenseSeatAssignmentService _assignments;
    private readonly IOptions<BillingLicenseReconciliationOptions> _options;
    private readonly IServiceOperationExecutor _operations;

    public BillingLicenseReconciler(
        ITenantLicenseService licenses,
        ILicenseSeatAssignmentService assignments,
        IOptions<BillingLicenseReconciliationOptions>? options = null,
        IServiceOperationExecutor? operations = null)
    {
        _licenses = licenses;
        _assignments = assignments;
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

        if (IsRefund(request))
            return await ApplyRefundAsync(tenantId, request, subscription, ct).ConfigureAwait(false);

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
        var existing = await FindExistingLicenseAsync(tenantId, subscription, request.LicenseKey, ct).ConfigureAwait(false);
        var now = request.EffectiveUtc ?? DateTimeOffset.UtcNow;
        var starts = subscription.CurrentPeriodStartsUtc ?? now;
        var expires = subscription.CurrentPeriodEndsUtc ?? now.AddDays(request.RenewalPeriodDays ?? _options.Value.DefaultRenewalPeriodDays);
        var seatPolicy = await ResolveSeatPolicyAsync(tenantId, request, subscription, plan, existing, ct).ConfigureAwait(false);
        var metadata = BuildMetadata(subscription, plan, request.Metadata, existing?.Metadata);
        ApplySeatPolicyMetadata(metadata, seatPolicy);

        if (existing is null)
        {
            var created = await _licenses.GrantLicenseAsync(
                tenantId,
                new GrantTenantLicenseRequest
                {
                    PlanKey = plan.PlanKey,
                    Status = ResolveRuntimeStatus(subscription),
                    CommercialStatus = ResolveCommercialStatus(subscription),
                    SeatLimit = seatPolicy.EffectiveSeatLimit,
                    Entitlements = plan.Entitlements,
                    StartsUtc = starts,
                    ExpiresUtc = expires,
                    ProviderName = subscription.ProviderName ?? subscription.Price?.ProviderName,
                    ProviderCustomerId = request.ProviderCustomerId,
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
                SeatLimit = seatPolicy.EffectiveSeatLimit,
                StartsUtc = existing.StartsUtc,
                ExpiresUtc = existing.ExpiresUtc is { } current && current > expires ? current : expires,
                ProviderName = subscription.ProviderName ?? subscription.Price?.ProviderName,
                ProviderCustomerId = request.ProviderCustomerId ?? existing.ProviderCustomerId,
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
        return await ApplyTerminationAsync(
            tenantId,
            request,
            subscription,
            NormalizeBehavior(request.CancellationBehavior, _options.Value.CancellationBehavior),
            "Billing subscription canceled.",
            "billingCancellationUtc",
            ct).ConfigureAwait(false);
    }

    private async Task<BillingLicenseReconciliationResult> ApplyRefundAsync(
        Guid tenantId,
        ReconcileBillingLicenseRequest request,
        BillingSubscriptionInfo subscription,
        CancellationToken ct)
    {
        return await ApplyTerminationAsync(
            tenantId,
            request,
            subscription,
            NormalizeBehavior(request.RefundBehavior, _options.Value.RefundBehavior),
            "Billing payment refunded.",
            "billingRefundUtc",
            ct).ConfigureAwait(false);
    }

    private async Task<BillingLicenseReconciliationResult> ApplyTerminationAsync(
        Guid tenantId,
        ReconcileBillingLicenseRequest request,
        BillingSubscriptionInfo subscription,
        string behavior,
        string revokeReason,
        string effectiveMetadataKey,
        CancellationToken ct)
    {
        if (!IsAny(
                behavior,
                BillingLicenseCancellationBehaviors.Suspend,
                BillingLicenseCancellationBehaviors.Expire,
                BillingLicenseCancellationBehaviors.Revoke,
                BillingLicenseCancellationBehaviors.ScheduleRevocation))
        {
            throw new BillingException($"Unknown termination behavior '{behavior}'.");
        }

        var existing = await FindExistingLicenseAsync(tenantId, subscription, request.LicenseKey, ct).ConfigureAwait(false);
        if (existing is null)
            return new BillingLicenseReconciliationResult(BillingLicenseReconciliationActions.NoOp, null, "No matching license was found.");

        if (behavior == BillingLicenseCancellationBehaviors.Revoke)
        {
            await _licenses.RevokeLicenseAsync(tenantId, existing.LicenseId, revokeReason, ct).ConfigureAwait(false);
            var revoked = (await _licenses.ListTenantLicensesAsync(tenantId, ct).ConfigureAwait(false))
                .FirstOrDefault(x => x.LicenseId == existing.LicenseId);
            return new BillingLicenseReconciliationResult(BillingLicenseReconciliationActions.Revoked, revoked, null);
        }

        var metadata = new Dictionary<string, string>(existing.Metadata, StringComparer.OrdinalIgnoreCase)
        {
            [effectiveMetadataKey] = (request.EffectiveUtc ?? DateTimeOffset.UtcNow).ToString("O")
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
        var existing = await FindExistingLicenseAsync(tenantId, subscription, request.LicenseKey, ct).ConfigureAwait(false);
        if (existing is null)
            return new BillingLicenseReconciliationResult(BillingLicenseReconciliationActions.NoOp, null, "No matching license was found.");

        var behavior = NormalizeBehavior(request.PaymentFailureBehavior, _options.Value.PaymentFailureBehavior);
        if (!IsAny(
                behavior,
                BillingLicensePaymentFailureBehaviors.Suspend,
                BillingLicensePaymentFailureBehaviors.Expire,
                BillingLicensePaymentFailureBehaviors.Grace,
                BillingLicensePaymentFailureBehaviors.NoOp))
        {
            throw new BillingException($"Unknown payment failure behavior '{behavior}'.");
        }

        if (behavior == BillingLicensePaymentFailureBehaviors.NoOp)
            return new BillingLicenseReconciliationResult(BillingLicenseReconciliationActions.NoOp, existing, null);

        var effectiveUtc = request.EffectiveUtc ?? DateTimeOffset.UtcNow;
        var isGrace = behavior == BillingLicensePaymentFailureBehaviors.Grace;
        var graceStartsUtc = existing.ExpiresUtc is { } expiresUtc && expiresUtc > effectiveUtc
            ? expiresUtc
            : effectiveUtc;
        var graceEndsUtc = isGrace
            ? graceStartsUtc.AddDays(request.GracePeriodDays ?? _options.Value.DefaultGracePeriodDays)
            : existing.GraceEndsUtc;

        var updated = await _licenses.UpdateLicenseAsync(
            tenantId,
            existing.LicenseId,
            new UpdateTenantLicenseRequest
            {
                Status = behavior == BillingLicensePaymentFailureBehaviors.Expire
                    ? LicenseStatuses.Expired
                    : isGrace ? LicenseStatuses.Grace : LicenseStatuses.Suspended,
                CommercialStatus = isGrace ? LicenseCommercialStatuses.Grace : LicenseCommercialStatuses.PastDue,
                ProviderStatus = subscription.ProviderStatus ?? subscription.Status,
                ExpiresUtc = behavior == BillingLicensePaymentFailureBehaviors.Expire
                    ? effectiveUtc
                    : existing.ExpiresUtc,
                GraceEndsUtc = graceEndsUtc
            },
            ct).ConfigureAwait(false);

        return new BillingLicenseReconciliationResult(
            isGrace
                ? BillingLicenseReconciliationActions.Grace
                : behavior == BillingLicensePaymentFailureBehaviors.Expire
                ? BillingLicenseReconciliationActions.Expired
                : BillingLicenseReconciliationActions.Suspended,
            updated,
            null);
    }

    private async Task<TenantLicenseInfo?> FindExistingLicenseAsync(
        Guid tenantId,
        BillingSubscriptionInfo subscription,
        Guid? licenseKey,
        CancellationToken ct)
    {
        if (licenseKey is { } explicitLicenseKey)
        {
            var explicitLicense = await _licenses.GetLicenseByKeyAsync(tenantId, explicitLicenseKey, ct).ConfigureAwait(false);
            return explicitLicense ?? throw new BillingException($"License '{explicitLicenseKey}' was not found for provider reconciliation.");
        }

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

    private static bool IsRefund(ReconcileBillingLicenseRequest request)
        => IsAny(request.EventType, "payment.refunded", "charge.refunded", "payment.disputed", "charge.dispute.created");

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
        IReadOnlyDictionary<string, string>? requestMetadata,
        IReadOnlyDictionary<string, string>? existingMetadata)
    {
        var metadata = new Dictionary<string, string>(
            BillingPriceReferenceInfo.NormalizeMetadata(existingMetadata),
            StringComparer.OrdinalIgnoreCase)
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

    private async Task<SeatPolicyResolution> ResolveSeatPolicyAsync(
        Guid tenantId,
        ReconcileBillingLicenseRequest request,
        BillingSubscriptionInfo subscription,
        BillingPricePlanMappingOptions plan,
        TenantLicenseInfo? existing,
        CancellationToken ct)
    {
        var requested = subscription.SeatQuantity ?? plan.SeatLimit ?? existing?.SeatLimit;
        if (requested is <= 0)
            throw new BillingException("Billing seat quantity must be greater than zero.");

        var licenseType = existing?.Metadata.TryGetValue("billingLicenseType", out var currentType) == true
            ? currentType
            : requested is <= 1 ? "individual" : "multi-user";

        if (requested is > 1)
            licenseType = "multi-user";

        var effective = requested;
        if (string.Equals(licenseType, "multi-user", StringComparison.OrdinalIgnoreCase) && effective is not null)
            effective = Math.Max(effective.Value, Math.Max(1, _options.Value.MinimumMultiUserSeats));

        var assignedCount = existing is null
            ? 0
            : (await _assignments.ListAssignmentsAsync(tenantId, existing.LicenseId, ct).ConfigureAwait(false)).Count;
        var behavior = NormalizeBehavior(request.SeatDecreaseBehavior, _options.Value.SeatDecreaseBehavior);
        if (!IsAny(
                behavior,
                BillingLicenseSeatDecreaseBehaviors.PreserveAssignments,
                BillingLicenseSeatDecreaseBehaviors.AllowOverAssigned,
                BillingLicenseSeatDecreaseBehaviors.Reject))
        {
            throw new BillingException($"Unknown seat decrease behavior '{behavior}'.");
        }

        var state = "within-limit";

        if (effective is not null && effective.Value < assignedCount)
        {
            if (behavior == BillingLicenseSeatDecreaseBehaviors.Reject)
                throw new BillingException($"Cannot reduce license seats to {effective.Value} while {assignedCount} seats are assigned.");

            if (behavior == BillingLicenseSeatDecreaseBehaviors.PreserveAssignments)
            {
                effective = assignedCount;
                state = "adjusted-to-assignments";
            }
            else if (behavior == BillingLicenseSeatDecreaseBehaviors.AllowOverAssigned)
            {
                state = "over-assigned";
            }
        }

        return new SeatPolicyResolution(requested, effective, assignedCount, licenseType, behavior, state);
    }

    private static void ApplySeatPolicyMetadata(Dictionary<string, string> metadata, SeatPolicyResolution policy)
    {
        metadata["billingLicenseType"] = policy.LicenseType;
        metadata["billingSeatDecreaseBehavior"] = policy.DecreaseBehavior;
        metadata["billingAssignedSeatCount"] = policy.AssignedSeatCount.ToString();
        metadata["billingSeatState"] = policy.State;

        if (policy.RequestedSeatLimit is { } requested)
            metadata["billingRequestedSeatLimit"] = requested.ToString();
        if (policy.EffectiveSeatLimit is { } effective)
            metadata["billingEffectiveSeatLimit"] = effective.ToString();
    }

    private static bool IsAny(string? value, params string[] values)
    {
        var normalized = BillingModes.NormalizeKnown(value, string.Empty);
        return values.Any(x => string.Equals(normalized, BillingModes.NormalizeKnown(x, string.Empty), StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeBehavior(string? value, string defaultValue)
        => BillingModes.NormalizeKnown(value, defaultValue);

    private sealed record SeatPolicyResolution(
        int? RequestedSeatLimit,
        int? EffectiveSeatLimit,
        int AssignedSeatCount,
        string LicenseType,
        string DecreaseBehavior,
        string State);
}
