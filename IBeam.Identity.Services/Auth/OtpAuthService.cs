using IBeam.Identity.Abstractions.Exceptions;
using IBeam.Identity.Abstractions.Interfaces;
using IBeam.Identity.Abstractions.Models;
using IBeam.Identity.Services.Utils;

namespace IBeam.Identity.Services.Auth;

public sealed class OtpAuthService : IIdentityOtpAuthService
{
    private readonly IIdentityUserStore _users;
    private readonly ITenantMembershipStore _tenants;
    private readonly ITenantProvisioningService _tenantProvisioning;
    private readonly ITokenService _tokens;
    private readonly IOtpService _otpService;
    private readonly IOtpChallengeStore _otpChallengeStore;

    public OtpAuthService(
        IIdentityUserStore users,
        ITenantMembershipStore tenants,
        ITenantProvisioningService tenantProvisioning,
        ITokenService tokens,
        IOtpService otpService,
        IOtpChallengeStore otpChallengeStore)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _tenantProvisioning = tenantProvisioning ?? throw new ArgumentNullException(nameof(tenantProvisioning));
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _otpService = otpService ?? throw new ArgumentNullException(nameof(otpService));
        _otpChallengeStore = otpChallengeStore ?? throw new ArgumentNullException(nameof(otpChallengeStore));
    }

    public async Task<OtpChallengeResult> RegisterUserViaOtpAsync(string destination, Guid? tenantId = null, CancellationToken ct = default)
    {
        IdentityUtils.ThrowIfNullOrWhiteSpace(destination, nameof(destination));

        var (channel, normalized) = IdentityUtils.NormalizeDestination(destination);
        var request = new OtpChallengeRequest(channel, normalized, SenderPurpose.UserRegistration, tenantId);
        return await _otpService.CreateChallengeAsync(request, ct);
    }

    public async Task<AuthResultResponse> CompleteUserRegistrationViaOtpAsync(
        string challengeId,
        string code,
        string destination,
        string? displayName = null,
        CancellationToken ct = default)
    {
        IdentityUtils.ThrowIfNullOrWhiteSpace(challengeId, nameof(challengeId));
        IdentityUtils.ThrowIfNullOrWhiteSpace(code, nameof(code));
        IdentityUtils.ThrowIfNullOrWhiteSpace(destination, nameof(destination));

        var (channel, normalizedDestination) = IdentityUtils.NormalizeDestination(destination);

        var verifyResult = await _otpService.VerifyAsync(new OtpVerifyRequest(challengeId, code), ct);
        if (!verifyResult.Success)
            throw new IdentityValidationException("OTP verification failed.");

        var challenge = await _otpChallengeStore.GetAsync(challengeId, ct);
        if (challenge is null)
            throw new IdentityValidationException("OTP challenge not found.");
        if (challenge.Purpose != SenderPurpose.UserRegistration)
            throw new IdentityValidationException("OTP challenge purpose is invalid for registration.");

        var (_, normalizedFromChallenge) = IdentityUtils.NormalizeDestination(challenge.Destination);
        if (!string.Equals(normalizedFromChallenge, normalizedDestination, StringComparison.OrdinalIgnoreCase))
            throw new IdentityValidationException("OTP destination mismatch.");

        IdentityUser? user = channel == SenderChannel.Email
            ? await _users.FindByEmailAsync(normalizedDestination, ct)
            : await _users.FindByPhoneAsync(normalizedDestination, ct);

        var createdNewUser = false;
        if (user is null)
        {
            var createRequest = channel == SenderChannel.Email
                ? new RegisterUserRequest(normalizedDestination, null, string.Empty, displayName)
                : new RegisterUserRequest(null, normalizedDestination, string.Empty, displayName);

            var createResult = await _users.CreateAsync(createRequest, ct);
            if (!createResult.Succeeded || createResult.User is null)
                throw new IdentityValidationException("User creation failed.", createResult.Errors);

            user = createResult.User;
            createdNewUser = true;
        }

        var activeTenants = (await _tenants.GetTenantsForUserAsync(user.UserId, ct))
            .Where(t => t.IsActive)
            .ToList();

        if (createdNewUser || activeTenants.Count == 0)
        {
            var email = channel == SenderChannel.Email ? normalizedDestination : user.Email;
            var createdTenantId = await _tenantProvisioning.CreateTenantForNewUserAsync(user.UserId, email, ct);
            activeTenants = (await _tenants.GetTenantsForUserAsync(user.UserId, ct))
                .Where(t => t.IsActive)
                .ToList();

            if (!activeTenants.Any(t => t.TenantId == createdTenantId))
                throw new IdentityProviderException("Tenant provisioning completed but membership could not be resolved.");
        }

        if (activeTenants.Count == 1)
        {
            var tenant = activeTenants[0];
            var claims = BuildBaseClaims(user);
            AddTenantClaims(claims, tenant.TenantId);
            AddRoleClaims(claims, tenant.Roles);
            var token = await _tokens.CreateAccessTokenAsync(user.UserId, tenant.TenantId, claims, ct);
            return AuthResultResponse.WithToken(token);
        }

        var defaultTenantId = await _tenants.GetDefaultTenantIdAsync(user.UserId, ct);
        if (defaultTenantId.HasValue)
        {
            var defaultTenant = activeTenants.FirstOrDefault(t => t.TenantId == defaultTenantId.Value);
            if (defaultTenant is not null)
            {
                var claims = BuildBaseClaims(user);
                AddTenantClaims(claims, defaultTenant.TenantId);
                AddRoleClaims(claims, defaultTenant.Roles);
                var token = await _tokens.CreateAccessTokenAsync(user.UserId, defaultTenant.TenantId, claims, ct);
                return AuthResultResponse.WithToken(token);
            }
        }

        var preClaims = BuildBaseClaims(user);
        preClaims.Add(new ClaimItem("pt", "1"));
        var preToken = await _tokens.CreatePreTenantTokenAsync(user.UserId, preClaims, ct);
        return AuthResultResponse.RequiresSelection(preToken.AccessToken, activeTenants);
    }

    private static List<ClaimItem> BuildBaseClaims(IdentityUser user)
    {
        var claims = new List<ClaimItem>
        {
            new("sub", user.UserId.ToString("D")),
            new("uid", user.UserId.ToString("D")),
        };

        if (!string.IsNullOrWhiteSpace(user.Email))
            claims.Add(new ClaimItem("email", user.Email));

        if (!string.IsNullOrWhiteSpace(user.PhoneNumber))
            claims.Add(new ClaimItem("phone_number", user.PhoneNumber!));

        return claims;
    }

    private static void AddTenantClaims(List<ClaimItem> claims, Guid tenantId)
    {
        claims.Add(new ClaimItem("tid", tenantId.ToString("D")));
    }

    private static void AddRoleClaims(List<ClaimItem> claims, IEnumerable<string>? roles)
    {
        if (roles is null) return;

        foreach (var role in roles.Where(r => !string.IsNullOrWhiteSpace(r)))
            claims.Add(new ClaimItem("role", role));
    }
}
