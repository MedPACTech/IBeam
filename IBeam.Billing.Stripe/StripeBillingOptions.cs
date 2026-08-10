namespace IBeam.Billing.Stripe;

public sealed class StripeBillingOptions
{
    public const string SectionName = "IBeam:Billing:Stripe";

    public string SecretKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SecretKey))
            throw new InvalidOperationException($"{SectionName}:SecretKey is required.");
        if (string.IsNullOrWhiteSpace(WebhookSecret))
            throw new InvalidOperationException($"{SectionName}:WebhookSecret is required.");
    }
}
