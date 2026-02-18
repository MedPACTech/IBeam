namespace IBeam.Identity.Abstractions.Interfaces;

using IBeam.Identity.Abstractions.Models;

public interface IIdentityRegistrationService
{
    Task<IdentityUser> RegisterAsync(RegisterUserRequest request, CancellationToken ct = default);
}

public interface IIdentityAuthService
{
    Task<IdentityUser> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<TokenResult> IssueTokenAsync(TokenRequest request, CancellationToken ct = default);
}

public interface IOtpService
{
    Task<OtpChallengeResult> CreateChallengeAsync(OtpChallengeRequest request, CancellationToken ct = default);
    Task<OtpVerifyResult> VerifyAsync(OtpVerifyRequest request, CancellationToken ct = default);
}

public interface ITenantSelectionService
{
    Task<IReadOnlyList<TenantInfo>> GetTenantsForUserAsync(Guid userId, CancellationToken ct = default);
    Task<TenantSelectionResult> SelectTenantAsync(TenantSelectionRequest request, CancellationToken ct = default);
}
