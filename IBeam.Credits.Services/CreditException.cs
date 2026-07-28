namespace IBeam.Credits.Services;

public sealed class CreditException : InvalidOperationException
{
    public CreditException(string message)
        : base(message)
    {
    }
}
