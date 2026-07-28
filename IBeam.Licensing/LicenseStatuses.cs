namespace IBeam.Licensing;

public static class LicenseStatuses
{
    public const string Active = "active";
    public const string Trialing = "trialing";
    public const string Grace = "grace";
    public const string Manual = "manual";
    public const string Suspended = "suspended";
    public const string Revoked = "revoked";
    public const string Expired = "expired";
}

public static class LicenseCommercialStatuses
{
    public const string Unknown = "unknown";
    public const string Paid = "paid";
    public const string Trial = "trial";
    public const string Grace = "grace";
    public const string PastDue = "past-due";
    public const string Canceled = "canceled";
    public const string Manual = "manual";
    public const string SupportGranted = "support-granted";
}

public static class LicenseRuntimeStatuses
{
    public const string Active = "active";
    public const string Trialing = "trialing";
    public const string Grace = "grace";
    public const string Manual = "manual";
    public const string NotStarted = "not-started";
    public const string Suspended = "suspended";
    public const string Revoked = "revoked";
    public const string Expired = "expired";
    public const string Unknown = "unknown";
}
