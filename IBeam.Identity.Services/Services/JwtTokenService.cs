using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using IBeam.Identity.Core.Auth.Contracts;
using IBeam.Identity.Core.Auth.Interfaces;
using IBeam.Identity.Core.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace IBeam.Identity.Services;

public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
        _options.Validate();
    }

    public AuthTokenResponse CreateAccessToken(
        string userId,
        string? email,
        IEnumerable<string> roles,
        IEnumerable<Claim>? extraClaims = null)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(ClaimTypes.NameIdentifier, userId)
        };

        if (!string.IsNullOrWhiteSpace(email))
        {
            claims.Add(new(JwtRegisteredClaimNames.Email, email));
            claims.Add(new(ClaimTypes.Email, email));
            claims.Add(new(ClaimTypes.Name, email));
        }

        foreach (var role in roles)
            claims.Add(new(ClaimTypes.Role, role));

        if (extraClaims != null)
            claims.AddRange(extraClaims);

        return Sign(claims, now, expires);
    }

    public AuthTokenResponse CreatePreTenantToken(
        string userId,
        string? email,
        IEnumerable<Claim>? extraClaims = null)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(_options.PreTenantTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(ClaimTypes.NameIdentifier, userId),

            // marker claim: this token is ONLY for tenant selection
            new("pt", "1")
        };

        if (!string.IsNullOrWhiteSpace(email))
        {
            claims.Add(new(JwtRegisteredClaimNames.Email, email));
            claims.Add(new(ClaimTypes.Email, email));
            claims.Add(new(ClaimTypes.Name, email));
        }

        if (extraClaims != null)
            claims.AddRange(extraClaims);

        return Sign(claims, now, expires);
    }

    private AuthTokenResponse Sign(List<Claim> claims, DateTimeOffset now, DateTimeOffset expires)
    {
        var keyBytes = Encoding.UTF8.GetBytes(_options.SigningKey);
        var signingKey = new SymmetricSecurityKey(keyBytes);

        if (!string.IsNullOrWhiteSpace(_options.KeyId))
            signingKey.KeyId = _options.KeyId;

        var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: creds);

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);

        return new AuthTokenResponse(jwt, expires);
    }
}
