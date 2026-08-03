namespace IBeam.Identity.Exceptions;

public sealed class OAuthProtocolException : Exception
{
    public OAuthProtocolException(string error, string description, string? redirectUri = null, string? state = null)
        : base(description)
    {
        Error = error;
        RedirectUri = redirectUri;
        State = state;
    }

    public string Error { get; }
    public string? RedirectUri { get; }
    public string? State { get; }
}
