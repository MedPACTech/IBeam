using System.Security.Claims;
using IBeam.Identity.Core.Auth.Contracts;

namespace IBeam.Identity.Core.Auth.Interfaces;

public interface IJwtTokenService
{
    AuthTokenResponse CreateAccessToken(
        string userId,
        string? email,
        IEnumerable<string> roles,
        IEnumerable<Claim>? extraClaims = null);

    // NEW: short-lived token that proves authentication but has no tenant context
    AuthTokenResponse CreatePreTenantToken(
        string userId,
        string? email,
        IEnumerable<Claim>? extraClaims = null);
}
