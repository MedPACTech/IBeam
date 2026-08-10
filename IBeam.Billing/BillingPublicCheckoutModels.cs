namespace IBeam.Billing;

public interface IBillingPublicCheckoutService
{
    Task<BillingCheckoutStartInfo> StartCheckoutAsync(StartBillingCheckoutRequest request, CancellationToken ct = default);
    Task<BillingPurchasePublicStatusInfo?> GetPurchaseStatusAsync(Guid purchaseId, string statusToken, CancellationToken ct = default);
}

public sealed class StartBillingCheckoutRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public string OfferKey { get; set; } = string.Empty;
    public int? TotalSeats { get; set; }
    public string BuyerEmail { get; set; } = string.Empty;
    public string? ProviderName { get; set; }
    public Uri? SuccessUrl { get; set; }
    public Uri? CancelUrl { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
}

public sealed record BillingCheckoutStartInfo(
    Guid PurchaseId,
    string Status,
    string OfferKey,
    int LicenseQuantity,
    int TotalSeats,
    string Currency,
    decimal TotalAmount,
    Uri CheckoutUrl,
    string StatusToken,
    DateTimeOffset StatusTokenExpiresUtc);

public sealed record BillingPurchasePublicStatusInfo(
    Guid PurchaseId,
    string Status,
    string OfferKey,
    int TotalSeats,
    string Currency,
    decimal TotalAmount,
    string NextAction,
    DateTimeOffset? ExpiresUtc);

public static class BillingPurchaseNextActions
{
    public const string ContinueCheckout = "continue-checkout";
    public const string AwaitPayment = "await-payment";
    public const string CreateAccount = "create-account";
    public const string Complete = "complete";
    public const string RestartCheckout = "restart-checkout";
    public const string ContactSupport = "contact-support";

    public static string FromStatus(string status)
        => BillingPurchaseStatuses.Normalize(status) switch
        {
            BillingPurchaseStatuses.Initiated => ContinueCheckout,
            BillingPurchaseStatuses.AwaitingPayment => AwaitPayment,
            BillingPurchaseStatuses.Paid => CreateAccount,
            BillingPurchaseStatuses.Fulfilled => CreateAccount,
            BillingPurchaseStatuses.Claimed => Complete,
            BillingPurchaseStatuses.Expired => RestartCheckout,
            BillingPurchaseStatuses.Canceled => RestartCheckout,
            BillingPurchaseStatuses.Failed => RestartCheckout,
            BillingPurchaseStatuses.Refunded => ContactSupport,
            _ => ContactSupport
        };
}
