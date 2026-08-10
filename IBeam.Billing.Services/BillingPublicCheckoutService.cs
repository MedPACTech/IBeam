using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace IBeam.Billing.Services;

public sealed class BillingPublicCheckoutService : IBillingPublicCheckoutService
{
    private readonly IBillingOfferCatalogProvider _offers;
    private readonly IBillingPurchaseService _purchases;
    private readonly IBillingCheckoutGatewayResolver _gateways;
    private readonly BillingPublicCheckoutOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly IBillingCheckoutAttemptStore? _attempts;

    public BillingPublicCheckoutService(
        IBillingOfferCatalogProvider offers,
        IBillingPurchaseService purchases,
        IBillingCheckoutGatewayResolver gateways,
        IOptions<BillingPublicCheckoutOptions> options,
        TimeProvider? timeProvider = null,
        IBillingCheckoutAttemptStore? attempts = null)
    {
        _offers = offers;
        _purchases = purchases;
        _gateways = gateways;
        _options = options.Value;
        _options.Validate();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _attempts = attempts;
    }

    public async Task<BillingCheckoutStartInfo> StartCheckoutAsync(
        StartBillingCheckoutRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var idempotencyKey = BillingPriceReferenceInfo.NormalizeRequired(request.IdempotencyKey, nameof(request.IdempotencyKey));
        if (idempotencyKey.Length > 200)
            throw new BillingException("idempotencyKey cannot exceed 200 characters.");
        var successUrl = RequireAllowedReturnUrl(request.SuccessUrl, nameof(request.SuccessUrl));
        var cancelUrl = RequireAllowedReturnUrl(request.CancelUrl, nameof(request.CancelUrl));
        var offer = await _offers.GetOfferAsync(request.OfferKey, ct).ConfigureAwait(false)
                    ?? throw new BillingException("The selected offer is not available.");
        var quote = offer.Quote(request.TotalSeats);
        var providerName = BillingPriceReferenceInfo.NormalizeOptional(request.ProviderName)?.ToLowerInvariant()
                           ?? _options.DefaultProviderName;
        var gateway = _gateways.Resolve(providerName);
        var providerPrice = offer.ProviderPrices.FirstOrDefault(x =>
            string.Equals(x.ProviderName, providerName, StringComparison.OrdinalIgnoreCase));
        var correlationId = CreateCorrelationId(providerName, idempotencyKey);
        var now = _timeProvider.GetUtcNow();
        var purchase = await _purchases.CreatePendingPurchaseAsync(
            new CreatePendingBillingPurchaseRequest
            {
                CorrelationId = correlationId,
                BuyerEmail = request.BuyerEmail,
                OfferKey = offer.Key,
                ProductKey = offer.ProductKey,
                PlanKey = offer.PlanKey,
                TotalSeats = quote.TotalSeats,
                Currency = quote.Currency,
                AmountSubtotal = quote.TotalAmount,
                AmountTax = 0m,
                AmountTotal = quote.TotalAmount,
                ProviderName = providerName,
                ExpiresUtc = now.AddMinutes(_options.PurchaseLifetimeMinutes),
                Metadata = request.Metadata
            },
            ct).ConfigureAwait(false);

        if (!string.Equals(purchase.OfferKey, offer.Key, StringComparison.OrdinalIgnoreCase) ||
            purchase.TotalSeats != quote.TotalSeats ||
            !string.Equals(purchase.BuyerEmail, BillingPurchaseInfo.NormalizeEmail(request.BuyerEmail), StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingException("The idempotency key is already associated with a different checkout request.");
        }

        if (string.Equals(purchase.Status, BillingPurchaseStatuses.AwaitingPayment, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(purchase.ProviderCheckoutSessionId))
        {
            var existingSession = await gateway.GetCheckoutSessionAsync(purchase.ProviderCheckoutSessionId, ct).ConfigureAwait(false);
            if (existingSession?.CheckoutUrl is null)
                throw new BillingException("The existing checkout session is no longer available.");
            return CreateStartInfo(purchase, quote, existingSession.CheckoutUrl, now);
        }

        if (!string.Equals(purchase.Status, BillingPurchaseStatuses.Initiated, StringComparison.OrdinalIgnoreCase))
            throw new BillingException($"Purchase is already in status '{purchase.Status}'.");

        var session = await gateway.CreateCheckoutSessionAsync(
            CreateBillingCheckoutSessionRequest.Create(
                purchase.CorrelationId,
                offer.Key,
                quote.TotalSeats,
                successUrl,
                cancelUrl,
                purchase.BuyerEmail,
                providerPrice?.PriceId,
                new Dictionary<string, string> { ["purchaseId"] = purchase.PurchaseId.ToString("D") }),
            ct).ConfigureAwait(false);
        if (session.CheckoutUrl is null)
            throw new BillingException("The checkout provider did not return a hosted checkout URL.");

        await _purchases.ApplyProviderUpdateAsync(
            purchase.PurchaseId,
            new ApplyBillingPurchaseProviderUpdateRequest
            {
                ProviderName = providerName,
                ProviderEventId = $"checkout-created:{session.CheckoutSessionId}",
                EventType = "checkout.session.created",
                Status = BillingPurchaseStatuses.AwaitingPayment,
                ProviderCheckoutSessionId = session.CheckoutSessionId,
                ProviderCustomerId = session.ProviderCustomerId,
                ProviderSubscriptionId = session.ProviderSubscriptionId,
                OccurredUtc = now
            },
            ct).ConfigureAwait(false);

        if (_attempts is not null)
        {
            await _attempts.SaveAttemptAsync(
                BillingCheckoutAttemptInfo.Create(
                    purchase.PurchaseId,
                    providerName,
                    session.CheckoutSessionId,
                    session.Status,
                    now,
                    session.Metadata),
                ct).ConfigureAwait(false);
        }

        return CreateStartInfo(purchase, quote, session.CheckoutUrl, now);
    }

    public async Task<BillingPurchasePublicStatusInfo?> GetPurchaseStatusAsync(
        Guid purchaseId,
        string statusToken,
        CancellationToken ct = default)
    {
        if (purchaseId == Guid.Empty || !ValidateStatusToken(purchaseId, statusToken))
            return null;

        var purchase = await _purchases.GetPurchaseAsync(purchaseId, ct).ConfigureAwait(false);
        return purchase is null
            ? null
            : new BillingPurchasePublicStatusInfo(
                purchase.PurchaseId,
                purchase.Status,
                purchase.OfferKey,
                purchase.TotalSeats,
                purchase.Currency,
                purchase.AmountTotal,
                BillingPurchaseNextActions.FromStatus(purchase.Status),
                purchase.ExpiresUtc);
    }

    private Uri RequireAllowedReturnUrl(Uri? uri, string parameterName)
    {
        if (uri is null)
            throw new BillingException($"{parameterName} is required.");

        var origin = BillingPublicCheckoutOptions.NormalizeOrigin(uri);
        if (!_options.AllowedReturnOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
            throw new BillingException($"{parameterName} is not an allowed return URL.");
        return uri;
    }

    private BillingCheckoutStartInfo CreateStartInfo(
        BillingPurchaseInfo purchase,
        BillingOfferQuoteInfo quote,
        Uri checkoutUrl,
        DateTimeOffset now)
    {
        var tokenExpiresUtc = now.AddMinutes(_options.StatusTokenLifetimeMinutes);
        return new BillingCheckoutStartInfo(
            purchase.PurchaseId,
            BillingPurchaseStatuses.AwaitingPayment,
            purchase.OfferKey,
            quote.LicenseQuantity,
            quote.TotalSeats,
            quote.Currency,
            quote.TotalAmount,
            checkoutUrl,
            CreateStatusToken(purchase.PurchaseId, tokenExpiresUtc),
            tokenExpiresUtc);
    }

    private Guid CreateCorrelationId(string providerName, string idempotencyKey)
    {
        var bytes = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(_options.StatusTokenSigningKey),
            Encoding.UTF8.GetBytes($"checkout:{providerName}:{idempotencyKey}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private string CreateStatusToken(Guid purchaseId, DateTimeOffset expiresUtc)
    {
        Span<byte> payload = stackalloc byte[24];
        purchaseId.TryWriteBytes(payload[..16]);
        BinaryPrimitives.WriteInt64BigEndian(payload[16..], expiresUtc.ToUnixTimeSeconds());
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(_options.StatusTokenSigningKey), payload);
        return $"{Base64Url(payload)}.{Base64Url(signature)}";
    }

    private bool ValidateStatusToken(Guid purchaseId, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;
        var parts = token.Split('.');
        if (parts.Length != 2)
            return false;

        try
        {
            var payload = FromBase64Url(parts[0]);
            var signature = FromBase64Url(parts[1]);
            if (payload.Length != 24 || signature.Length != 32)
                return false;
            var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(_options.StatusTokenSigningKey), payload);
            if (!CryptographicOperations.FixedTimeEquals(signature, expected))
                return false;
            var tokenPurchaseId = new Guid(payload.AsSpan(0, 16));
            var expires = DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(16, 8)));
            return tokenPurchaseId == purchaseId && expires > _timeProvider.GetUtcNow();
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Base64Url(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }
}
