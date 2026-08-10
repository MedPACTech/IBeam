using IBeam.Licensing;

namespace IBeam.Billing.Licensing;

public sealed class BillingLicenseReconciliationOptions
{
    public const string SectionName = "IBeam:Billing:Licensing";

    public List<BillingPricePlanMappingOptions> PriceMappings { get; set; } = [];
    public int DefaultRenewalPeriodDays { get; set; } = 30;
    public int DefaultGracePeriodDays { get; set; } = 7;
    public int MinimumMultiUserSeats { get; set; } = 3;
    public string CancellationBehavior { get; set; } = BillingLicenseCancellationBehaviors.Suspend;
    public string PaymentFailureBehavior { get; set; } = BillingLicensePaymentFailureBehaviors.Suspend;
    public string RefundBehavior { get; set; } = BillingLicenseRefundBehaviors.Revoke;
    public string SeatDecreaseBehavior { get; set; } = BillingLicenseSeatDecreaseBehaviors.PreserveAssignments;
}

public sealed class BillingPricePlanMappingOptions
{
    public string ProviderName { get; set; } = string.Empty;
    public string PriceId { get; set; } = string.Empty;
    public string PlanKey { get; set; } = string.Empty;
    public int? SeatLimit { get; set; }
    public List<string> Entitlements { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = [];
}

public sealed class ReconcileBillingLicenseRequest
{
    public BillingSubscriptionInfo Subscription { get; set; } = null!;
    public Guid? LicenseKey { get; set; }
    public string? ProviderCustomerId { get; set; }
    public string? EventType { get; set; }
    public string? PlanKey { get; set; }
    public int? RenewalPeriodDays { get; set; }
    public string? CancellationBehavior { get; set; }
    public string? PaymentFailureBehavior { get; set; }
    public string? RefundBehavior { get; set; }
    public string? SeatDecreaseBehavior { get; set; }
    public int? GracePeriodDays { get; set; }
    public DateTimeOffset? EffectiveUtc { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
}

public sealed record BillingLicenseReconciliationResult(
    string Action,
    TenantLicenseInfo? License,
    string? Reason);

public static class BillingLicenseReconciliationActions
{
    public const string Created = "created";
    public const string Renewed = "renewed";
    public const string Suspended = "suspended";
    public const string Expired = "expired";
    public const string Revoked = "revoked";
    public const string ScheduledRevocation = "scheduled-revocation";
    public const string Grace = "grace";
    public const string NoOp = "no-op";
}

public static class BillingLicenseCancellationBehaviors
{
    public const string Suspend = "suspend";
    public const string Expire = "expire";
    public const string Revoke = "revoke";
    public const string ScheduleRevocation = "schedule-revocation";
}

public static class BillingLicensePaymentFailureBehaviors
{
    public const string Suspend = "suspend";
    public const string Expire = "expire";
    public const string Grace = "grace";
    public const string NoOp = "no-op";
}

public static class BillingLicenseRefundBehaviors
{
    public const string Suspend = "suspend";
    public const string Expire = "expire";
    public const string Revoke = "revoke";
    public const string ScheduleRevocation = "schedule-revocation";
}

public static class BillingLicenseSeatDecreaseBehaviors
{
    public const string PreserveAssignments = "preserve-assignments";
    public const string AllowOverAssigned = "allow-over-assigned";
    public const string Reject = "reject";
}
