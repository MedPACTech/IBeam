namespace IBeam.Billing.Services;

public sealed class BillingCheckoutGatewayResolver : IBillingCheckoutGatewayResolver
{
    private readonly IReadOnlyDictionary<string, IBillingCheckoutGateway> _gateways;

    public BillingCheckoutGatewayResolver(IEnumerable<IBillingCheckoutGateway> gateways)
    {
        ArgumentNullException.ThrowIfNull(gateways);

        var resolved = new Dictionary<string, IBillingCheckoutGateway>(StringComparer.OrdinalIgnoreCase);
        foreach (var gateway in gateways)
        {
            var providerName = BillingPriceReferenceInfo.NormalizeRequired(
                gateway.ProviderName,
                nameof(gateway.ProviderName));
            if (!resolved.TryAdd(providerName, gateway))
                throw new InvalidOperationException($"Billing checkout gateway '{providerName}' is registered more than once.");
        }

        _gateways = resolved;
        ProviderNames = resolved.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<string> ProviderNames { get; }

    public IBillingCheckoutGateway Resolve(string providerName)
    {
        var normalized = BillingPriceReferenceInfo.NormalizeRequired(providerName, nameof(providerName));
        if (_gateways.TryGetValue(normalized, out var gateway))
            return gateway;

        throw new BillingException($"Billing checkout gateway '{normalized}' is not registered.");
    }
}
