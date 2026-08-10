using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace IBeam.Billing.Stripe;

public sealed class StripeBillingCheckoutGateway : IBillingCheckoutGateway
{
    public const string Name = "stripe";
    private const string SignatureHeader = "Stripe-Signature";
    private readonly IStripeBillingApiClient _client;
    private readonly StripeBillingOptions _options;

    public StripeBillingCheckoutGateway(
        IStripeBillingApiClient client,
        IOptions<StripeBillingOptions> options)
    {
        _client = client;
        _options = options.Value;
        _options.Validate();
    }

    public string ProviderName => Name;

    public async Task<BillingCheckoutSessionInfo> CreateCheckoutSessionAsync(
        CreateBillingCheckoutSessionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ProviderPriceId))
            throw new BillingException("A Stripe price id is required for checkout.");
        var priceId = BillingPriceReferenceInfo.NormalizeRequired(request.ProviderPriceId, nameof(request.ProviderPriceId));
        var metadata = BuildMetadata(request);
        var options = new SessionCreateOptions
        {
            Mode = "subscription",
            SuccessUrl = request.SuccessUrl.AbsoluteUri,
            CancelUrl = request.CancelUrl.AbsoluteUri,
            CustomerEmail = request.BuyerEmail,
            ClientReferenceId = request.CorrelationId.ToString("D"),
            Metadata = metadata,
            SubscriptionData = new SessionSubscriptionDataOptions { Metadata = new Dictionary<string, string>(metadata) },
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Price = priceId,
                    Quantity = request.TotalSeats
                }
            ]
        };
        var session = await _client.CreateCheckoutSessionAsync(
            options,
            new RequestOptions { IdempotencyKey = request.CorrelationId.ToString("N") },
            ct).ConfigureAwait(false);
        return MapSession(request.CorrelationId, session);
    }

    public async Task<BillingCheckoutSessionInfo?> GetCheckoutSessionAsync(
        string checkoutSessionId,
        CancellationToken ct = default)
    {
        var sessionId = BillingPriceReferenceInfo.NormalizeRequired(checkoutSessionId, nameof(checkoutSessionId));
        var session = await _client.GetCheckoutSessionAsync(sessionId, ct).ConfigureAwait(false);
        var correlationId = Guid.TryParse(session.ClientReferenceId, out var parsed) ? parsed : ReadGuid(session.Metadata, "correlationId");
        if (correlationId == Guid.Empty)
            throw new BillingException("Stripe Checkout Session does not contain an IBeam correlation id.");
        return MapSession(correlationId, session);
    }

    public async Task<BillingCustomerPortalSessionInfo> CreateCustomerPortalSessionAsync(
        CreateBillingCustomerPortalSessionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var session = await _client.CreatePortalSessionAsync(
            new global::Stripe.BillingPortal.SessionCreateOptions
            {
                Customer = request.ProviderCustomerId,
                ReturnUrl = request.ReturnUrl.AbsoluteUri
            },
            new RequestOptions { IdempotencyKey = request.CorrelationId.ToString("N") },
            ct).ConfigureAwait(false);
        return BillingCustomerPortalSessionInfo.Create(
            request.CorrelationId,
            Name,
            session.Id,
            new Uri(session.Url),
            metadata: request.Metadata);
    }

    public Task<BillingVerifiedWebhookInfo> VerifyWebhookAsync(
        BillingWebhookRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Headers.TryGetValue(SignatureHeader, out var signature) || string.IsNullOrWhiteSpace(signature))
            throw new BillingException("Stripe-Signature header is required.");

        var json = Encoding.UTF8.GetString(request.Body.Span);
        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                json,
                signature,
                _options.WebhookSecret,
                tolerance: 300,
                throwOnApiVersionMismatch: false);
        }
        catch (StripeException)
        {
            throw new BillingException("Stripe webhook signature verification failed.");
        }

        using var document = JsonDocument.Parse(json);
        var eventType = NormalizeEventType(stripeEvent.Type);
        var metadata = ReadMetadata(document.RootElement);
        AddTotalSeats(document.RootElement, metadata);
        var objectId = ReadString(document.RootElement, "data", "object", "id");
        var checkoutSessionId = string.Equals(stripeEvent.Type, EventTypes.CheckoutSessionCompleted, StringComparison.Ordinal)
            ? objectId
            : ReadString(document.RootElement, "data", "object", "checkout_session");
        var subscriptionId = ReadString(document.RootElement, "data", "object", "subscription")
                             ?? ReadString(document.RootElement, "data", "object", "parent", "subscription_details", "subscription")
                             ?? (stripeEvent.Type.StartsWith("customer.subscription.", StringComparison.Ordinal) ? objectId : null);

        return Task.FromResult(BillingVerifiedWebhookInfo.Create(
            Name,
            stripeEvent.Id,
            eventType,
            stripeEvent.Created,
            providerCustomerId: ReadString(document.RootElement, "data", "object", "customer"),
            providerSubscriptionId: subscriptionId,
            providerCheckoutSessionId: checkoutSessionId,
            metadata: metadata));
    }

    private static Dictionary<string, string> BuildMetadata(CreateBillingCheckoutSessionRequest request)
    {
        var metadata = new Dictionary<string, string>(request.Metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["correlationId"] = request.CorrelationId.ToString("D"),
            ["offerKey"] = request.OfferKey,
            ["totalSeats"] = request.TotalSeats.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        return metadata;
    }

    private static BillingCheckoutSessionInfo MapSession(Guid correlationId, Session session)
        => BillingCheckoutSessionInfo.Create(
            correlationId,
            Name,
            session.Id,
            MapSessionStatus(session.Status),
            string.IsNullOrWhiteSpace(session.Url) ? null : new Uri(session.Url),
            session.CustomerId,
            session.SubscriptionId,
            session.ExpiresAt,
            session.Metadata);

    private static string MapSessionStatus(string? status)
        => status?.Trim().ToLowerInvariant() switch
        {
            "open" => BillingCheckoutSessionStatuses.Open,
            "complete" => BillingCheckoutSessionStatuses.Completed,
            "expired" => BillingCheckoutSessionStatuses.Expired,
            null or "" => BillingCheckoutSessionStatuses.Pending,
            _ => BillingCheckoutSessionStatuses.Failed
        };

    private static string NormalizeEventType(string stripeEventType)
        => stripeEventType switch
        {
            EventTypes.CheckoutSessionCompleted => BillingCommerceEventTypes.CheckoutCompleted,
            EventTypes.PaymentIntentSucceeded => BillingCommerceEventTypes.PaymentSucceeded,
            EventTypes.PaymentIntentPaymentFailed or EventTypes.InvoicePaymentFailed => BillingCommerceEventTypes.PaymentFailed,
            EventTypes.InvoicePaid => BillingCommerceEventTypes.SubscriptionRenewed,
            EventTypes.CustomerSubscriptionUpdated => BillingCommerceEventTypes.SubscriptionSeatsChanged,
            EventTypes.CustomerSubscriptionDeleted => BillingCommerceEventTypes.SubscriptionCanceled,
            EventTypes.ChargeRefunded => BillingCommerceEventTypes.PaymentRefunded,
            EventTypes.ChargeDisputeCreated => BillingCommerceEventTypes.PaymentDisputed,
            _ => stripeEventType
        };

    private static Dictionary<string, string> ReadMetadata(JsonElement root)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        MergeMetadata(root, metadata, "data", "object", "parent", "subscription_details", "metadata");
        MergeMetadata(root, metadata, "data", "object", "subscription_details", "metadata");
        MergeMetadata(root, metadata, "data", "object", "metadata");
        return metadata;
    }

    private static void MergeMetadata(JsonElement root, IDictionary<string, string> metadata, params string[] path)
    {
        if (!TryRead(root, out var element, path) || element.ValueKind != JsonValueKind.Object)
            return;
        foreach (var property in element.EnumerateObject())
            if (property.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.Value.GetString()))
                metadata[property.Name] = property.Value.GetString()!;
    }

    private static void AddTotalSeats(JsonElement root, IDictionary<string, string> metadata)
    {
        if (metadata.ContainsKey("totalSeats"))
            return;
        if (TryRead(root, out var quantity, "data", "object", "items", "data", "0", "quantity") &&
            quantity.TryGetInt64(out var seats))
        {
            metadata["totalSeats"] = seats.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private static string? ReadString(JsonElement root, params string[] path)
        => TryRead(root, out var element, path) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static Guid ReadGuid(IReadOnlyDictionary<string, string> metadata, string key)
        => metadata.TryGetValue(key, out var value) && Guid.TryParse(value, out var parsed) ? parsed : Guid.Empty;

    private static bool TryRead(JsonElement current, out JsonElement value, params string[] path)
    {
        foreach (var segment in path)
        {
            if (current.ValueKind == JsonValueKind.Array && int.TryParse(segment, out var index))
            {
                if (index < 0 || index >= current.GetArrayLength())
                {
                    value = default;
                    return false;
                }
                current = current[index];
            }
            else if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                value = default;
                return false;
            }
        }
        value = current;
        return true;
    }
}
