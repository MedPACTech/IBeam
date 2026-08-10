namespace IBeam.Billing;

public interface IBillingCheckoutGateway
{
    string ProviderName { get; }

    Task<BillingCheckoutSessionInfo> CreateCheckoutSessionAsync(
        CreateBillingCheckoutSessionRequest request,
        CancellationToken ct = default);

    Task<BillingCheckoutSessionInfo?> GetCheckoutSessionAsync(
        string checkoutSessionId,
        CancellationToken ct = default);

    Task<BillingCustomerPortalSessionInfo> CreateCustomerPortalSessionAsync(
        CreateBillingCustomerPortalSessionRequest request,
        CancellationToken ct = default);

    Task<BillingVerifiedWebhookInfo> VerifyWebhookAsync(
        BillingWebhookRequest request,
        CancellationToken ct = default);
}

public interface IBillingCheckoutGatewayResolver
{
    IReadOnlyList<string> ProviderNames { get; }
    IBillingCheckoutGateway Resolve(string providerName);
}

public sealed record CreateBillingCheckoutSessionRequest(
    Guid CorrelationId,
    string OfferKey,
    int TotalSeats,
    Uri SuccessUrl,
    Uri CancelUrl,
    string? BuyerEmail,
    string? ProviderPriceId,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static CreateBillingCheckoutSessionRequest Create(
        Guid correlationId,
        string offerKey,
        int totalSeats,
        Uri successUrl,
        Uri cancelUrl,
        string? buyerEmail = null,
        string? providerPriceId = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (correlationId == Guid.Empty)
            throw new ArgumentException("Correlation id is required.", nameof(correlationId));
        if (totalSeats <= 0)
            throw new ArgumentOutOfRangeException(nameof(totalSeats), "Total seats must be positive.");

        return new CreateBillingCheckoutSessionRequest(
            correlationId,
            BillingPriceReferenceInfo.NormalizeRequired(offerKey, nameof(offerKey)),
            totalSeats,
            NormalizeAbsoluteHttpUri(successUrl, nameof(successUrl)),
            NormalizeAbsoluteHttpUri(cancelUrl, nameof(cancelUrl)),
            NormalizeEmail(buyerEmail),
            BillingPriceReferenceInfo.NormalizeOptional(providerPriceId),
            BillingPriceReferenceInfo.NormalizeMetadata(metadata));
    }

    public static Uri NormalizeAbsoluteHttpUri(Uri value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (!value.IsAbsoluteUri ||
            (!string.Equals(value.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(value.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("An absolute HTTP or HTTPS URL is required.", parameterName);
        }

        return value;
    }

    private static string? NormalizeEmail(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}

public sealed record BillingCheckoutSessionInfo(
    Guid CorrelationId,
    string ProviderName,
    string CheckoutSessionId,
    string Status,
    Uri? CheckoutUrl,
    string? ProviderCustomerId,
    string? ProviderSubscriptionId,
    DateTimeOffset? ExpiresUtc,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static BillingCheckoutSessionInfo Create(
        Guid correlationId,
        string providerName,
        string checkoutSessionId,
        string status,
        Uri? checkoutUrl = null,
        string? providerCustomerId = null,
        string? providerSubscriptionId = null,
        DateTimeOffset? expiresUtc = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (correlationId == Guid.Empty)
            throw new ArgumentException("Correlation id is required.", nameof(correlationId));

        return new BillingCheckoutSessionInfo(
            correlationId,
            BillingPriceReferenceInfo.NormalizeRequired(providerName, nameof(providerName)).ToLowerInvariant(),
            BillingPriceReferenceInfo.NormalizeRequired(checkoutSessionId, nameof(checkoutSessionId)),
            BillingCheckoutSessionStatuses.Normalize(status),
            checkoutUrl is null ? null : CreateBillingCheckoutSessionRequest.NormalizeAbsoluteHttpUri(checkoutUrl, nameof(checkoutUrl)),
            BillingPriceReferenceInfo.NormalizeOptional(providerCustomerId),
            BillingPriceReferenceInfo.NormalizeOptional(providerSubscriptionId),
            expiresUtc,
            BillingPriceReferenceInfo.NormalizeMetadata(metadata));
    }
}

public sealed record CreateBillingCustomerPortalSessionRequest(
    Guid CorrelationId,
    string ProviderCustomerId,
    Uri ReturnUrl,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static CreateBillingCustomerPortalSessionRequest Create(
        Guid correlationId,
        string providerCustomerId,
        Uri returnUrl,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (correlationId == Guid.Empty)
            throw new ArgumentException("Correlation id is required.", nameof(correlationId));

        return new CreateBillingCustomerPortalSessionRequest(
            correlationId,
            BillingPriceReferenceInfo.NormalizeRequired(providerCustomerId, nameof(providerCustomerId)),
            CreateBillingCheckoutSessionRequest.NormalizeAbsoluteHttpUri(returnUrl, nameof(returnUrl)),
            BillingPriceReferenceInfo.NormalizeMetadata(metadata));
    }
}

public sealed record BillingCustomerPortalSessionInfo(
    Guid CorrelationId,
    string ProviderName,
    string PortalSessionId,
    Uri PortalUrl,
    DateTimeOffset? ExpiresUtc,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static BillingCustomerPortalSessionInfo Create(
        Guid correlationId,
        string providerName,
        string portalSessionId,
        Uri portalUrl,
        DateTimeOffset? expiresUtc = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (correlationId == Guid.Empty)
            throw new ArgumentException("Correlation id is required.", nameof(correlationId));

        return new BillingCustomerPortalSessionInfo(
            correlationId,
            BillingPriceReferenceInfo.NormalizeRequired(providerName, nameof(providerName)).ToLowerInvariant(),
            BillingPriceReferenceInfo.NormalizeRequired(portalSessionId, nameof(portalSessionId)),
            CreateBillingCheckoutSessionRequest.NormalizeAbsoluteHttpUri(portalUrl, nameof(portalUrl)),
            expiresUtc,
            BillingPriceReferenceInfo.NormalizeMetadata(metadata));
    }
}

public sealed record BillingWebhookRequest(
    ReadOnlyMemory<byte> Body,
    IReadOnlyDictionary<string, string> Headers,
    DateTimeOffset ReceivedUtc)
{
    public static BillingWebhookRequest Create(
        ReadOnlyMemory<byte> body,
        IReadOnlyDictionary<string, string>? headers = null,
        DateTimeOffset? receivedUtc = null)
    {
        if (body.IsEmpty)
            throw new ArgumentException("Webhook body is required.", nameof(body));

        return new BillingWebhookRequest(
            body,
            headers?
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .ToDictionary(x => x.Key.Trim(), x => x.Value?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            receivedUtc ?? DateTimeOffset.UtcNow);
    }
}

public sealed record BillingVerifiedWebhookInfo(
    string ProviderName,
    string ProviderEventId,
    string EventType,
    DateTimeOffset OccurredUtc,
    string? ProviderCustomerId,
    string? ProviderSubscriptionId,
    string? ProviderCheckoutSessionId,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static BillingVerifiedWebhookInfo Create(
        string providerName,
        string providerEventId,
        string eventType,
        DateTimeOffset occurredUtc,
        string? providerCustomerId = null,
        string? providerSubscriptionId = null,
        string? providerCheckoutSessionId = null,
        IReadOnlyDictionary<string, string>? metadata = null)
        => new(
            BillingPriceReferenceInfo.NormalizeRequired(providerName, nameof(providerName)).ToLowerInvariant(),
            BillingPriceReferenceInfo.NormalizeRequired(providerEventId, nameof(providerEventId)),
            BillingPriceReferenceInfo.NormalizeRequired(eventType, nameof(eventType)),
            occurredUtc,
            BillingPriceReferenceInfo.NormalizeOptional(providerCustomerId),
            BillingPriceReferenceInfo.NormalizeOptional(providerSubscriptionId),
            BillingPriceReferenceInfo.NormalizeOptional(providerCheckoutSessionId),
            BillingPriceReferenceInfo.NormalizeMetadata(metadata));
}

public sealed record BillingProviderBindingInfo(
    Guid BindingId,
    string ProviderName,
    string BindingType,
    string ProviderReferenceId,
    bool IsActive,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? RetiredUtc,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static BillingProviderBindingInfo Create(
        Guid bindingId,
        string providerName,
        string bindingType,
        string providerReferenceId,
        bool isActive = true,
        DateTimeOffset? createdUtc = null,
        DateTimeOffset? retiredUtc = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (bindingId == Guid.Empty)
            throw new ArgumentException("Binding id is required.", nameof(bindingId));
        if (isActive && retiredUtc is not null)
            throw new ArgumentException("An active provider binding cannot have a retired timestamp.", nameof(retiredUtc));

        return new BillingProviderBindingInfo(
            bindingId,
            BillingPriceReferenceInfo.NormalizeRequired(providerName, nameof(providerName)).ToLowerInvariant(),
            BillingProviderBindingTypes.Normalize(bindingType),
            BillingPriceReferenceInfo.NormalizeRequired(providerReferenceId, nameof(providerReferenceId)),
            isActive,
            createdUtc ?? DateTimeOffset.UtcNow,
            retiredUtc,
            BillingPriceReferenceInfo.NormalizeMetadata(metadata));
    }
}

public static class BillingCheckoutSessionStatuses
{
    public const string Pending = "pending";
    public const string Open = "open";
    public const string Completed = "completed";
    public const string Expired = "expired";
    public const string Canceled = "canceled";
    public const string Failed = "failed";

    public static string Normalize(string value)
        => BillingPriceReferenceInfo.NormalizeRequired(value, nameof(value)).Trim().ToLowerInvariant() switch
        {
            Pending => Pending,
            Open => Open,
            Completed => Completed,
            Expired => Expired,
            Canceled => Canceled,
            Failed => Failed,
            _ => throw new ArgumentException($"Checkout session status '{value}' is not supported.", nameof(value))
        };
}

public static class BillingProviderBindingTypes
{
    public const string CheckoutSession = "checkout-session";
    public const string Customer = "customer";
    public const string Subscription = "subscription";
    public const string Invoice = "invoice";

    public static string Normalize(string value)
        => BillingPriceReferenceInfo.NormalizeRequired(value, nameof(value))
            .Trim()
            .Replace('_', '-')
            .ToLowerInvariant() switch
        {
            CheckoutSession => CheckoutSession,
            Customer => Customer,
            Subscription => Subscription,
            Invoice => Invoice,
            _ => throw new ArgumentException($"Provider binding type '{value}' is not supported.", nameof(value))
        };
}
