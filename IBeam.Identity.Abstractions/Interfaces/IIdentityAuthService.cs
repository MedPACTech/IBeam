namespace IBeam.Identity.Abstractions.Interfaces;

using IBeam.Identity.Abstractions.Models;

public interface IIdentityAuthService
{
    Task<IdentityUser> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<TokenResult> IssueTokenAsync(TokenRequest request, CancellationToken ct = default);
}
