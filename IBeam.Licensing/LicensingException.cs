namespace IBeam.Licensing;

public class LicensingException : Exception
{
    public LicensingException(string message)
        : base(message)
    {
    }
}
