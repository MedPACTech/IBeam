using IBeam.Identity.Abstractions.Exceptions;
using IBeam.Identity.Abstractions.Interfaces;
using IBeam.Identity.Abstractions.Models;
using IBeam.Identity.Services.Utils;
using System.Security.Cryptography;
using System.Text;

namespace IBeam.Identity.Services.Auth;

public sealed class PasswordAuthService : IIdentityAuthService
{
    private readonly IIdentityUserStore _users;
    private readonly ITenantMembershipStore _tenants;
    private readonly ITenantProvisioningService _tenantProvisioning;
    private readonly ITokenService _tokens;
    private readonly IOtpChallengeStore _otpChallenges;
    private readonly IIdentityCommunicationSender _sender;

    public PasswordAuthService(
        IIdentityUserStore users,
        ITenantMembershipStore tenants,
        ITenantProvisioningService tenantProvisioning,
        ITokenService tokens,
        IOtpChallengeStore otpChallenges,
        IIdentityCommunicationSender sender)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _tenantProvisioning = tenantProvisioning ?? throw new ArgumentNullException(nameof(tenantProvisioning));
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _otpChallenges = otpChallenges ?? throw new ArgumentNullException(nameof(otpChallenges));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
    }

    public async Task RegisterAsync(RegisterUserRequest request, CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.Email)) throw new IdentityValidationException("Email is required.");
        if (string.IsNullOrWhiteSpace(request.Password)) throw new IdentityValidationException("Password is required.");

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

        if (activeTenants.Count == 1)
        {
            var t = activeTenants[0];
            var claims = BuildBaseClaims(user.UserId, user.Email);
            AddTenantClaims(claims, t.TenantId);
            AddRoleClaims(claims, t.Roles);
            var token = await _tokens.CreateAccessTokenAsync(user.UserId, t.TenantId, claims, ct);
            return AuthResultResponse.WithToken(token);
        }

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

        var preClaims = BuildBaseClaims(user.UserId, user.Email);
        preClaims.Add(new ClaimItem("pt", "1"));
        var pre = await _tokens.CreatePreTenantTokenAsync(user.UserId, preClaims, ct);
        return AuthResultResponse.RequiresSelection(pre.AccessToken, activeTenants);
    }

    public async Task<RequestPasswordResetResponse> StartEmailPasswordRegistrationAsync(
        string email,
        string? displayName = null,
        string? resetUrlBase = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new IdentityValidationException("Email is required.");

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var challengeId = Guid.NewGuid().ToString("D");
        var verificationToken = CreateVerificationToken();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(15);

        var challenge = new OtpChallengeRecord(
            ChallengeId: challengeId,
            Destination: normalizedEmail,
            Purpose: SenderPurpose.UserRegistration,
            CodeHash: Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N")))),
            ExpiresAt: expiresAt,
            AttemptCount: 0,
            TenantId: null,
            IsConsumed: true,
            VerificationToken: verificationToken,
            VerificationTokenExpiresAt: expiresAt);

        await _otpChallenges.SaveAsync(challenge, ct);

        var link = BuildResetLink(resetUrlBase, challengeId, verificationToken, normalizedEmail, displayName);
        await _sender.SendAsync(new IdentitySenderMessage
        {
            Channel = SenderChannel.Email,
            Destination = normalizedEmail,
            Purpose = SenderPurpose.UserRegistration,
            Subject = "Verify your email",
            Body = $"Click this link to finish account setup: {link}",
            ExpiresAt = expiresAt
        }, ct);

        return new RequestPasswordResetResponse(Accepted: true, ChallengeId: challengeId);
    }

    public async Task<AuthResultResponse> CompleteEmailPasswordRegistrationAsync(
        string email,
        string challengeId,
        string verificationToken,
        string newPassword,
        string? displayName = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new IdentityValidationException("Email is required.");
        if (string.IsNullOrWhiteSpace(challengeId))
            throw new IdentityValidationException("ChallengeId is required.");
        if (string.IsNullOrWhiteSpace(verificationToken))
            throw new IdentityValidationException("Verification token is required.");
        if (string.IsNullOrWhiteSpace(newPassword))
            throw new IdentityValidationException("New password is required.");

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var challenge = await _otpChallenges.GetAsync(challengeId, ct);
        if (challenge is null)
            throw new IdentityValidationException("Verification challenge not found.");
        if (!challenge.IsConsumed)
            throw new IdentityValidationException("Verification challenge is not active.");
        if (challenge.VerificationTokenExpiresAt is null || challenge.VerificationTokenExpiresAt <= DateTimeOffset.UtcNow)
            throw new IdentityValidationException("Verification token has expired.");
        if (!string.Equals(challenge.VerificationToken, verificationToken, StringComparison.Ordinal))
            throw new IdentityValidationException("Verification token is invalid.");
        if (!string.Equals(challenge.Destination, normalizedEmail, StringComparison.OrdinalIgnoreCase))
            throw new IdentityValidationException("Verification email does not match challenge.");

        var user = await _users.FindByEmailAsync(normalizedEmail, ct);
        var createdNewUser = false;
        if (user is null)
        {
            var createResult = await _users.CreateAsync(
                new RegisterUserRequest(normalizedEmail, null, string.Empty, displayName),
                ct);

            if (!createResult.Succeeded || createResult.User is null)
                throw new IdentityValidationException("User creation failed.", createResult.Errors);

            user = createResult.User;
            createdNewUser = true;
        }

        await _users.SetPasswordAsync(user.UserId, newPassword, ct);
        await _users.SetEmailConfirmedAsync(user.UserId, true, ct);

        return await BuildAuthResultAsync(user, createdNewUser, ct);
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
        var claims = BuildBaseClaims(userGuid, string.Empty);
        AddTenantClaims(claims, tenant.TenantId);
        AddRoleClaims(claims, tenant.Roles);
        var token = await _tokens.CreateAccessTokenAsync(userGuid, tenant.TenantId, claims, ct);
        return ToAuthTokenResponse(token);
    }

    public Task<AuthTokenResponse> SwitchTenantAsync(string userId, SelectTenantRequest request, CancellationToken ct = default)
        => SelectTenantAsync(userId, request, ct);

    private async Task<AuthResultResponse> BuildAuthResultAsync(IdentityUser user, bool createdNewUser, CancellationToken ct)
    {
        var activeTenants = (await _tenants.GetTenantsForUserAsync(user.UserId, ct))
            .Where(t => t.IsActive)
            .ToList();

        if (createdNewUser || activeTenants.Count == 0)
        {
            var createdTenantId = await _tenantProvisioning.CreateTenantForNewUserAsync(user.UserId, user.Email, ct);
            activeTenants = (await _tenants.GetTenantsForUserAsync(user.UserId, ct))
                .Where(t => t.IsActive)
                .ToList();

            if (!activeTenants.Any(t => t.TenantId == createdTenantId))
                throw new IdentityProviderException("Tenant provisioning completed but membership could not be resolved.");
        }

        if (activeTenants.Count == 1)
        {
            var tenant = activeTenants[0];
            var claims = BuildBaseClaims(user.UserId, user.Email);
            AddTenantClaims(claims, tenant.TenantId);
            AddRoleClaims(claims, tenant.Roles);
            var token = await _tokens.CreateAccessTokenAsync(user.UserId, tenant.TenantId, claims, ct);
            return AuthResultResponse.WithToken(token, createdNewUser);
        }

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
                return AuthResultResponse.WithToken(token, createdNewUser);
            }
        }

        var preClaims = BuildBaseClaims(user.UserId, user.Email);
        preClaims.Add(new ClaimItem("pt", "1"));
        var pre = await _tokens.CreatePreTenantTokenAsync(user.UserId, preClaims, ct);
        return AuthResultResponse.RequiresSelection(pre.AccessToken, activeTenants, createdNewUser);
    }

    private static string CreateVerificationToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static string BuildResetLink(
        string? resetUrlBase,
        string challengeId,
        string verificationToken,
        string email,
        string? displayName)
    {
        var baseUrl = string.IsNullOrWhiteSpace(resetUrlBase)
            ? "https://localhost:3000/reset-password"
            : resetUrlBase.Trim();

        var separator = baseUrl.Contains('?') ? "&" : "?";
        return $"{baseUrl}{separator}challengeId={Uri.EscapeDataString(challengeId)}&token={Uri.EscapeDataString(verificationToken)}&email={Uri.EscapeDataString(email)}&name={Uri.EscapeDataString(displayName ?? string.Empty)}";
    }

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
