using IBeam.Identity.Abstractions.Exceptions;
using IBeam.Identity.Abstractions.Interfaces;
using IBeam.Identity.Abstractions.Models;
using IBeam.Identity.Services.Otp;

namespace IBeam.Identity.Services.Auth;

public sealed class OtpAuthService : IIdentityOtpAuthService
    {
    private readonly IIdentityUserStore _users;
    private readonly ITenantMembershipStore _tenants;
    private readonly ITokenService _tokens;
    private readonly IOtpService _otpService;
    private readonly IOtpChallengeStore _otpChallengeStore;

    public OtpAuthService(
        IIdentityUserStore users,
        ITenantMembershipStore tenants,
        ITokenService tokens,
        IOtpService otpService,
        IOtpChallengeStore otpChallengeStore)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _otpService = otpService ?? throw new ArgumentNullException(nameof(otpService));
        _otpChallengeStore = otpChallengeStore ?? throw new ArgumentNullException(nameof(otpChallengeStore));
    }

    public async Task<OtpChallengeResult> RegisterUserViaOtpAsync(string destination, Guid? tenantId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(destination))
            throw new IdentityValidationException("Destination is required.");
        var (channel, normalized) = NormalizeDestination(destination);
        var request = new OtpChallengeRequest(channel, normalized, OtpPurpose.UserRegistration, tenantId);
        return await _otpService.CreateChallengeAsync(request, ct);
    }

    public async Task<CreateUserResult> CompleteUserRegistrationViaOtpAsync(string challengeId, string code, string email, string? displayName = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(challengeId)) throw new IdentityValidationException("ChallengeId is required.");
        if (string.IsNullOrWhiteSpace(code)) throw new IdentityValidationException("Code is required.");
        if (string.IsNullOrWhiteSpace(email)) throw new IdentityValidationException("Email is required.");
        var verifyResult = await _otpService.VerifyAsync(new OtpVerifyRequest(challengeId, code), ct);
        if (!verifyResult.Success)
            throw new IdentityValidationException("OTP verification failed.");
        var userRequest = new RegisterUserRequest(email, string.Empty, displayName);
        var result = await _users.CreateAsync(userRequest, ct);
        return result;
    }

    public async Task<OtpChallengeResult> BeginOtpLoginAsync(string destination, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(destination))
            throw new IdentityValidationException("Destination is required.");
        var (channel, normalized) = NormalizeDestination(destination);
        var request = new OtpChallengeRequest(channel, normalized, OtpPurpose.LoginMfa, null);
        return await _otpService.CreateChallengeAsync(request, ct);
    }

    public async Task<AuthResultResponse> CompleteOtpLoginAsync(string challengeId, string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(challengeId)) throw new IdentityValidationException("ChallengeId is required.");
        if (string.IsNullOrWhiteSpace(code)) throw new IdentityValidationException("Code is required.");
        var verifyResult = await _otpService.VerifyAsync(new OtpVerifyRequest(challengeId, code), ct);
        if (!verifyResult.Success)
            throw new IdentityValidationException("OTP verification failed.");
        var challenge = await _otpChallengeStore.GetAsync(challengeId, ct);
        if (challenge is null)
            throw new IdentityValidationException("OTP challenge not found.");
        IdentityUser? user = null;
        if (challenge.Purpose != OtpPurpose.LoginMfa)
            throw new IdentityValidationException("OTP challenge is not for login.");
        if (challenge.Destination.Contains("@"))
        {
            user = await _users.FindByEmailAsync(challenge.Destination, ct);
        }
        else
        {
            user = await _users.FindByPhoneAsync(challenge.Destination, ct);
        }
        if (user is null)
            throw new IdentityUnauthorizedException("User not found for OTP login.");
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

    // Begin adding a new email (sends OTP to new email)
    public async Task<OtpChallengeResult> BeginAddEmailAsync(Guid userId, string newEmail, CancellationToken ct = default)
    {
        if (userId == Guid.Empty) throw new IdentityValidationException("UserId is required.");
        if (string.IsNullOrWhiteSpace(newEmail)) throw new IdentityValidationException("New email is required.");
        var (channel, normalized) = NormalizeDestination(newEmail);
        if (channel != OtpChannel.Email) throw new IdentityValidationException("Destination must be a valid email.");
        var request = new OtpChallengeRequest(channel, normalized, OtpPurpose.EmailVerification, null);
        return await _otpService.CreateChallengeAsync(request, ct);
    }

    // Complete adding a new email (verifies OTP and updates user)
    public async Task<bool> CompleteAddEmailAsync(Guid userId, string challengeId, string code, string newEmail, CancellationToken ct = default)
    {
        if (userId == Guid.Empty) throw new IdentityValidationException("UserId is required.");
        if (string.IsNullOrWhiteSpace(challengeId)) throw new IdentityValidationException("ChallengeId is required.");
        if (string.IsNullOrWhiteSpace(code)) throw new IdentityValidationException("Code is required.");
        if (string.IsNullOrWhiteSpace(newEmail)) throw new IdentityValidationException("New email is required.");
        var verifyResult = await _otpService.VerifyAsync(new OtpVerifyRequest(challengeId, code), ct);
        if (!verifyResult.Success) throw new IdentityValidationException("OTP verification failed.");
        await _users.UpdateEmailAsync(userId, newEmail, ct);
        return true;
    }

    // Begin adding a new phone (sends OTP to new phone)
    public async Task<OtpChallengeResult> BeginAddPhoneAsync(Guid userId, string newPhone, CancellationToken ct = default)
    {
        if (userId == Guid.Empty) throw new IdentityValidationException("UserId is required.");
        if (string.IsNullOrWhiteSpace(newPhone)) throw new IdentityValidationException("New phone is required.");
        var (channel, normalized) = NormalizeDestination(newPhone);
        if (channel != OtpChannel.Sms) throw new IdentityValidationException("Destination must be a valid phone number.");
        var request = new OtpChallengeRequest(channel, normalized, OtpPurpose.PhoneVerification, null);
        return await _otpService.CreateChallengeAsync(request, ct);
    }

    // Complete adding a new phone (verifies OTP and updates user)
    public async Task<bool> CompleteAddPhoneAsync(Guid userId, string challengeId, string code, string newPhone, CancellationToken ct = default)
    {
        if (userId == Guid.Empty) throw new IdentityValidationException("UserId is required.");
        if (string.IsNullOrWhiteSpace(challengeId)) throw new IdentityValidationException("ChallengeId is required.");
        if (string.IsNullOrWhiteSpace(code)) throw new IdentityValidationException("Code is required.");
        if (string.IsNullOrWhiteSpace(newPhone)) throw new IdentityValidationException("New phone is required.");
        var verifyResult = await _otpService.VerifyAsync(new OtpVerifyRequest(challengeId, code), ct);
        if (!verifyResult.Success) throw new IdentityValidationException("OTP verification failed.");
        await _users.UpdatePhoneAsync(userId, newPhone, ct);
        return true;
    }

// Helper for normalization and channel detection. Move to utility later.
private static (OtpChannel channel, string normalized) NormalizeDestination(string destination)
    {
        destination = destination.Trim();
        var emailRegex = new System.Text.RegularExpressions.Regex(@"^[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var phoneRegex = new System.Text.RegularExpressions.Regex(@"^\+?[0-9 .-]+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (emailRegex.IsMatch(destination))
        {
            return (OtpChannel.Email, destination.ToUpperInvariant());
        }
        else if (phoneRegex.IsMatch(destination))
        {
            var normalized = destination.Replace("-", "").Replace(".", "").Replace(" ", "");
            if (normalized.StartsWith("+01"))
                normalized = normalized.Substring(3);
            else if (normalized.StartsWith("+"))
                normalized = normalized.Substring(1);
            return (OtpChannel.Sms, normalized);
        }
        else
        {
            throw new IdentityValidationException("Destination must be a valid email or phone number.");
        }
    }

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
