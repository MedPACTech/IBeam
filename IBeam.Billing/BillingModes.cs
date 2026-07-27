namespace IBeam.Billing;

public static class BillingModes
{
    public const string SelfServiceMonthly = "self-service-monthly";
    public const string AnnualContract = "annual-contract";
    public const string ManualInvoice = "manual-invoice";
    public const string Marketplace = "marketplace";
    public const string SupportManaged = "support-managed";
    public const string Unknown = "unknown";

    public static string Normalize(string? mode)
        => NormalizeKnown(mode, Unknown);

    public static bool IsKnown(string? mode)
        => Normalize(mode) is SelfServiceMonthly or AnnualContract or ManualInvoice or Marketplace or SupportManaged;

    internal static string NormalizeKnown(string? value, string defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        return value.Trim().ToLowerInvariant().Replace("_", "-");
    }
}

public static class BillingCustomerStatuses
{
    public const string Active = "active";
    public const string Inactive = "inactive";
    public const string Suspended = "suspended";
    public const string Deleted = "deleted";
    public const string Unknown = "unknown";

    public static string Normalize(string? status)
        => BillingModes.NormalizeKnown(status, Unknown);
}

public static class BillingSubscriptionStatuses
{
    public const string Active = "active";
    public const string Trialing = "trialing";
    public const string PastDue = "past-due";
    public const string Canceled = "canceled";
    public const string Incomplete = "incomplete";
    public const string Manual = "manual";
    public const string Unknown = "unknown";

    public static string Normalize(string? status)
        => BillingModes.NormalizeKnown(status, Unknown);
}

public static class BillingInvoiceStatuses
{
    public const string Draft = "draft";
    public const string Open = "open";
    public const string Paid = "paid";
    public const string Void = "void";
    public const string Uncollectible = "uncollectible";
    public const string Unknown = "unknown";

    public static string Normalize(string? status)
        => BillingModes.NormalizeKnown(status, Unknown);
}

public static class BillingProviderEventStatuses
{
    public const string Received = "received";
    public const string Processed = "processed";
    public const string Ignored = "ignored";
    public const string Failed = "failed";
    public const string Unknown = "unknown";

    public static string Normalize(string? status)
        => BillingModes.NormalizeKnown(status, Unknown);
}
