using IBeam.Identity.Models;

namespace IBeam.Identity.Interfaces;

public interface IApiCredentialAuthenticator
{
    Task<ApiCredentialAuthenticationResult> AuthenticateAsync(
        string apiKey,
        string? ipAddress = null,
        CancellationToken ct = default);
}
