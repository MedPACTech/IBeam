namespace IBeam.AccessControl;

public sealed class AccessControlException : Exception
{
    public AccessControlException(string message)
        : base(message)
    {
    }
}
