namespace IBeam.Billing;

public sealed class BillingException : InvalidOperationException
{
    public BillingException(string message)
        : base(message)
    {
    }
}
