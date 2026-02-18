namespace IBeam.Identity.Abstractions.Interfaces;

using IBeam.Identity.Abstractions.Models;

public interface IIdentityAuthService
{
    Task RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<AuthResultResponse> PasswordLoginAsync(PasswordLoginRequest request, CancellationToken ct = default);
    Task<AuthTokenResponse> SelectTenantAsync(string userId, SelectTenantRequest request, CancellationToken ct = default);
    Task<AuthTokenResponse> SwitchTenantAsync(string userId, SelectTenantRequest request, CancellationToken ct = default);
}

