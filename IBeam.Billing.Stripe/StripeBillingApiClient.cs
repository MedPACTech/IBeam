using Stripe;
using Stripe.Checkout;

namespace IBeam.Billing.Stripe;

public interface IStripeBillingApiClient
{
    Task<Session> CreateCheckoutSessionAsync(SessionCreateOptions options, RequestOptions requestOptions, CancellationToken ct);
    Task<Session> GetCheckoutSessionAsync(string sessionId, CancellationToken ct);
    Task<global::Stripe.BillingPortal.Session> CreatePortalSessionAsync(
        global::Stripe.BillingPortal.SessionCreateOptions options,
        RequestOptions requestOptions,
        CancellationToken ct);
}

public sealed class StripeBillingApiClient : IStripeBillingApiClient
{
    private readonly StripeClient _client;

    public StripeBillingApiClient(StripeClient client)
    {
        _client = client;
    }

    public Task<Session> CreateCheckoutSessionAsync(
        SessionCreateOptions options,
        RequestOptions requestOptions,
        CancellationToken ct)
        => new SessionService(_client).CreateAsync(options, requestOptions, ct);

    public Task<Session> GetCheckoutSessionAsync(string sessionId, CancellationToken ct)
        => new SessionService(_client).GetAsync(sessionId, cancellationToken: ct);

    public Task<global::Stripe.BillingPortal.Session> CreatePortalSessionAsync(
        global::Stripe.BillingPortal.SessionCreateOptions options,
        RequestOptions requestOptions,
        CancellationToken ct)
        => new global::Stripe.BillingPortal.SessionService(_client).CreateAsync(options, requestOptions, ct);
}
