using System.Security.Claims;
using IBeam.Identity.Core.Auth.Contracts;
using IBeam.Identity.Core.Auth.Interfaces;
using IBeam.Identity.Core.Tenants;
using IBeam.Identity.Storage.EntityFramework.Types;
using Microsoft.AspNetCore.Identity;

namespace IBeam.Identity.Storage.EntityFramework.Services;

public sealed class EntityFrameworkAuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly IJwtTokenService _jwt;
    private readonly ITenantMembershipStore _tenants;

    public EntityFrameworkAuthService(
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        IJwtTokenService jwt,
        ITenantMembershipStore tenants)
    {
        _users = users;
        _signIn = signIn;
        _jwt = jwt;
        _tenants = tenants;
    }

    public async Task RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            PhoneNumber = request.PhoneNumber
        };

        IdentityResult result = string.IsNullOrWhiteSpace(request.Password)
            ? await _users.CreateAsync(user)
            : await _users.CreateAsync(user, request.Password);

        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => $"{e.Code}:{e.Description}")));
    }

    public async Task<AuthResultResponse> PasswordLoginAsync(PasswordLoginRequest request, CancellationToken ct = default)
    {
        var user = await _users.FindByEmailAsync(request.Email);
        if (user is null)
            throw new UnauthorizedAccessException("Invalid credentials.");

        var result = await _signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
            throw new UnauthorizedAccessException("Invalid credentials.");

        // Multi-tenant behavior (same as AzureTable)
        var tenants = await _tenants.GetTenantsForUserAsync(user.Id.ToString(), ct);
        var active = tenants
            .Where(t => string.Equals(t.Status, "Active", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (active.Count == 0)
            throw new UnauthorizedAccessException("No active tenant membership.");

        if (active.Count == 1)
        {
            var t = active[0];
            var token = _jwt.CreateAccessToken(
                userId: user.Id.ToString(),
                email: user.Email,
                roles: t.Roles,
                extraClaims: new[] { new Claim("tid", t.TenantId.ToString("D")) });

            return AuthResultResponse.WithToken(token);
        }

        // If you implement defaults, call _tenants.GetDefaultTenantIdAsync(...) here.
        var pre = _jwt.CreatePreTenantToken(user.Id.ToString(), user.Email);
        return AuthResultResponse.RequiresSelection(pre.AccessToken, active);
    }

    public async Task<AuthTokenResponse> SelectTenantAsync(string userId, SelectTenantRequest request, CancellationToken ct = default)
    {
        var membership = await _tenants.GetTenantForUserAsync(userId, request.TenantId, ct);
        if (membership is null || !string.Equals(membership.Status, "Active", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Invalid tenant selection.");

        await _tenants.SetDefaultTenantAsync(userId, request.TenantId, ct);

        var user = await _users.FindByIdAsync(userId);
        if (user is null)
            throw new UnauthorizedAccessException("Invalid user.");

        return _jwt.CreateAccessToken(
            userId: user.Id.ToString(),
            email: user.Email,
            roles: membership.Roles,
            extraClaims: new[] { new Claim("tid", membership.TenantId.ToString("D")) });
    }

    public Task<AuthTokenResponse> SwitchTenantAsync(string userId, SelectTenantRequest request, CancellationToken ct = default)
    => SelectTenantAsync(userId, request, ct);

}
