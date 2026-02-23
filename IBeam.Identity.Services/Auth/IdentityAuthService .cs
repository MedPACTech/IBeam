using IBeam.Identity.Abstractions.Exceptions;
using IBeam.Identity.Abstractions.Interfaces;
using IBeam.Identity.Abstractions.Models;

namespace IBeam.Identity.Services.Auth;

public sealed class IdentityAuthService : IIdentityAuthService
{
    private readonly IIdentityUserStore _users;
    private readonly ITenantMembershipStore _tenants;
    private readonly ITokenService _tokens;

    public IdentityAuthService(
        IIdentityUserStore users,
        ITenantMembershipStore tenants,
        ITokenService tokens)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
    }

    public async Task RegisterAsync(RegisterUserRequest request, CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.Email)) throw new IdentityValidationException("Email is required.");
        if (string.IsNullOrWhiteSpace(request.Password)) throw new IdentityValidationException("Password is required.");

        // NOTE: adjust if your CreateAsync signature/result differs
        var result = await _users.CreateAsync(request, ct);

        if (!result.Succeeded)
            throw new IdentityValidationException("Registration failed.", result.Errors);

        if (result.User is null)
            throw new IdentityProviderException("UnknownProvider", "User store returned success but no user.");

    }

    public async Task<AuthResultResponse> PasswordLoginAsync(PasswordLoginRequest request, CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.Email)) throw new IdentityValidationException("Email is required.");
        if (string.IsNullOrWhiteSpace(request.Password)) throw new IdentityValidationException("Password is required.");

        var user = await _users.FindByEmailAsync(request.Email, ct);
        if (user is null)
            throw new IdentityUnauthorizedException("Invalid credentials.");

        var ok = await _users.ValidatePasswordAsync(request.Email, request.Password, ct);
        if (!ok)
            throw new IdentityUnauthorizedException("Invalid credentials.");

        var tenants = await _tenants.GetTenantsForUserAsync(user.UserId, ct);
        var activeTenants = tenants.Where(t => t.IsActive).ToList();

        if (activeTenants.Count == 0)
            throw new IdentityUnauthorizedException("No active tenant membership.");

        // 1 active tenant -> issue tenant-scoped token
        if (activeTenants.Count == 1)
        {
            var t = activeTenants[0];

            var claims = BuildBaseClaims(user.UserId, user.Email);
            AddTenantClaims(claims, t.TenantId);
            AddRoleClaims(claims, t.Roles);

            var token = await _tokens.CreateAccessTokenAsync(user.UserId, t.TenantId, claims, ct);
            return AuthResultResponse.WithToken(token);
        }

        // multiple tenants -> use default if present
        var defaultTenantId = await _tenants.GetDefaultTenantIdAsync(user.UserId, ct);
        if (defaultTenantId.HasValue)
        {
            var def = activeTenants.FirstOrDefault(x => x.TenantId == defaultTenantId.Value);
            if (def is not null)
            {
                var claims = BuildBaseClaims(user.UserId, user.Email);
                AddTenantClaims(claims, def.TenantId);
                AddRoleClaims(claims, def.Roles);

                var token = await _tokens.CreateAccessTokenAsync(user.UserId, def.TenantId, claims, ct);
                return AuthResultResponse.WithToken(token);
            }
        }

        // No default -> issue pre-tenant token and require selection
        var preClaims = BuildBaseClaims(user.UserId, user.Email);
        preClaims.Add(new ClaimItem("pt", "1"));

        var pre = await _tokens.CreatePreTenantTokenAsync(user.UserId, preClaims, ct);

        return AuthResultResponse.RequiresSelection(pre.AccessToken, activeTenants);
    }

    public async Task<AuthTokenResponse> SelectTenantAsync(string userId, SelectTenantRequest request, CancellationToken ct = default)
    {
        var userGuid = ParseUserId(userId);
        if (request is null) throw new ArgumentNullException(nameof(request));

        var tenant = await _tenants.GetTenantForUserAsync(userGuid, request.TenantId, ct);
        if (tenant is null || !tenant.IsActive)
            throw new IdentityUnauthorizedException("No active tenant membership.");

        if (request.SetAsDefault)
            await _tenants.SetDefaultTenantAsync(userGuid, request.TenantId, ct);

        var claims = BuildBaseClaims(userGuid, string.Empty); //TODO: May add identifiers later here
        AddTenantClaims(claims, tenant.TenantId);
        AddRoleClaims(claims, tenant.Roles);

        var token = await _tokens.CreateAccessTokenAsync(userGuid, tenant.TenantId, claims, ct);
        return ToAuthTokenResponse(token);
    }

    public Task<AuthTokenResponse> SwitchTenantAsync(string userId, SelectTenantRequest request, CancellationToken ct = default)
        => SelectTenantAsync(userId, request, ct);

    private static Guid ParseUserId(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new IdentityValidationException("userId is required.");

        if (!Guid.TryParse(userId, out var guid))
            throw new IdentityValidationException("userId must be a GUID.");

        return guid;
    }

    private static AuthTokenResponse ToAuthTokenResponse(TokenResult token)
        => new(token.AccessToken, token.ExpiresAt);

    private static List<ClaimItem> BuildBaseClaims(Guid userId, string? email)
    {
        var claims = new List<ClaimItem>
        {
            new("sub", userId.ToString("D")),
            new("uid", userId.ToString("D")),
        };

        //if (!string.IsNullOrWhiteSpace(email))
        //    claims.Add(new("email", email));

        return claims;
    }

    private static void AddTenantClaims(List<ClaimItem> claims, Guid tenantId)
    {
        claims.Add(new("tid", tenantId.ToString("D")));
    }

    private static void AddRoleClaims(List<ClaimItem> claims, IEnumerable<string>? roles)
    {
        if (roles is null) return;

        foreach (var r in roles.Where(x => !string.IsNullOrWhiteSpace(x)))
            claims.Add(new("role", r));
    }
}
