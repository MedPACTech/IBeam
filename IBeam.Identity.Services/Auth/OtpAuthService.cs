using IBeam.Identity.Exceptions;
using IBeam.Identity.Events;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace IBeam.Identity.Services.Auth;

public sealed class OtpAuthService : IIdentityOtpAuthService
{
    private readonly IIdentityUserStore _users;
    private readonly ITenantMembershipStore _tenants;
    private readonly ITenantProvisioningService _tenantProvisioning;
    private readonly ITokenService _tokens;
    private readonly IOtpService _otpService;
    private readonly IOtpChallengeStore _otpChallengeStore;
    private readonly IAuthEventPublisher _eventPublisher;
    private readonly IAuthLifecycleHook _lifecycleHook;
    private readonly IOptions<AuthEventOptions> _eventOptions;
    private readonly ILogger<OtpAuthService> _logger;

    public OtpAuthService(
        IIdentityUserStore users,
        ITenantMembershipStore tenants,
        ITenantProvisioningService tenantProvisioning,
        ITokenService tokens,
        IOtpService otpService,
        IOtpChallengeStore otpChallengeStore,
        IAuthEventPublisher eventPublisher,
        IAuthLifecycleHook lifecycleHook,
        IOptions<AuthEventOptions> eventOptions,
        ILogger<OtpAuthService> logger)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _tenantProvisioning = tenantProvisioning ?? throw new ArgumentNullException(nameof(tenantProvisioning));
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _otpService = otpService ?? throw new ArgumentNullException(nameof(otpService));
        _otpChallengeStore = otpChallengeStore ?? throw new ArgumentNullException(nameof(otpChallengeStore));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _lifecycleHook = lifecycleHook ?? throw new ArgumentNullException(nameof(lifecycleHook));
        _eventOptions = eventOptions ?? throw new ArgumentNullException(nameof(eventOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public OtpAuthService(
        IIdentityUserStore users,
        ITenantMembershipStore tenants,
        ITenantProvisioningService tenantProvisioning,
        ITokenService tokens,
        IOtpService otpService,
        IOtpChallengeStore otpChallengeStore)
        : this(
            users,
            tenants,
            tenantProvisioning,
            tokens,
            otpService,
            otpChallengeStore,
            new NoOpAuthEventPublisher(),
            new NoOpAuthLifecycleHook(),
            Microsoft.Extensions.Options.Options.Create(new AuthEventOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OtpAuthService>.Instance)
    {
    }

    public async Task<OtpChallengeResult> StartOtpAsync(string destination, Guid? tenantId = null, CancellationToken ct = default)
    {
        IdentityUtils.ThrowIfNullOrWhiteSpace(destination, nameof(destination));

        var (channel, normalized) = IdentityUtils.NormalizeDestination(destination);
        var request = new OtpChallengeRequest(channel, normalized, SenderPurpose.LoginMfa, tenantId);
        return await _otpService.CreateChallengeAsync(request, ct);
    }

    public async Task<AuthResultResponse> CompleteOtpAsync(
        string challengeId,
        string code,
        string destination,
        string? displayName = null,
        CancellationToken ct = default)
    {
        return await CompleteOtpInternalAsync(
            challengeId,
            code,
            destination,
            expectedPurposes: new[] { SenderPurpose.LoginMfa, SenderPurpose.UserRegistration },
            displayName,
            ct);
    }

    private async Task<AuthResultResponse> CompleteOtpInternalAsync(
        string challengeId,
        string code,
        string destination,
        IReadOnlyCollection<SenderPurpose> expectedPurposes,
        string? displayName,
        CancellationToken ct)
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

        // Safety guarantee: ensure the challenge is consumed after successful verification.
        if (!challenge.IsConsumed)
        {
            if (string.IsNullOrWhiteSpace(verifyResult.VerificationToken) || !verifyResult.ExpiresAt.HasValue)
                throw new IdentityProviderException("OTP verification did not return a valid verification token.");

            await _otpChallengeStore.MarkConsumedAsync(
                challengeId,
                verifyResult.VerificationToken,
                verifyResult.ExpiresAt.Value,
                ct);

            challenge = await _otpChallengeStore.GetAsync(challengeId, ct)
                ?? throw new IdentityProviderException("OTP challenge could not be reloaded after consume.");
        }

        if (!expectedPurposes.Contains(challenge.Purpose))
            throw new IdentityValidationException("OTP challenge purpose is invalid for authentication.");

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

            var userCreatedEvent = new AuthUserCreatedEvent
            {
                AuthUserId = user.UserId.ToString("D"),
                NormalizedEmail = string.IsNullOrWhiteSpace(user.Email) ? null : user.Email.Trim().ToLowerInvariant(),
                NormalizedPhone = string.IsNullOrWhiteSpace(user.PhoneNumber) ? null : user.PhoneNumber.Trim(),
                CorrelationId = challengeId,
                CausationId = challengeId,
                TraceId = ResolveTraceId()
            };
            userCreatedEvent.Metadata["idempotencyKey"] = $"{AuthUserCreatedEvent.TypeName}:{user.UserId:D}";

            await InvokeLifecycleAndPublishAsync(
                userCreatedEvent,
                evt => _lifecycleHook.OnAuthUserCreatedAsync(evt, ct),
                ct);
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

            var createdTenant = activeTenants.FirstOrDefault(t => t.TenantId == createdTenantId);
            var tenantCreatedEvent = new TenantCreatedEvent
            {
                TenantId = createdTenantId,
                TenantName = createdTenant?.Name,
                CorrelationId = challengeId,
                CausationId = challengeId,
                TraceId = ResolveTraceId()
            };
            tenantCreatedEvent.Metadata["idempotencyKey"] = $"{TenantCreatedEvent.TypeName}:{createdTenantId:D}";

            await InvokeLifecycleAndPublishAsync(
                tenantCreatedEvent,
                evt => _lifecycleHook.OnTenantCreatedAsync(evt, ct),
                ct);

            var linkedRole = createdTenant?.Roles?.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r));
            var tenantUserLinkedEvent = new TenantUserLinkedEvent
            {
                TenantId = createdTenantId,
                AuthUserId = user.UserId.ToString("D"),
                Role = linkedRole,
                UserTenantId = $"{createdTenantId:D}|{user.UserId:D}",
                CorrelationId = challengeId,
                CausationId = challengeId,
                TraceId = ResolveTraceId()
            };
            tenantUserLinkedEvent.Metadata["idempotencyKey"] =
                $"{TenantUserLinkedEvent.TypeName}:{createdTenantId:D}:{user.UserId:D}";

            await InvokeLifecycleAndPublishAsync(
                tenantUserLinkedEvent,
                evt => _lifecycleHook.OnTenantUserLinkedAsync(evt, ct),
                ct);
        }

        if (activeTenants.Count == 1)
        {
            var tenant = activeTenants[0];
            var claims = BuildBaseClaims(user);
            AddTenantClaims(claims, tenant.TenantId);
            AddRoleClaims(claims, tenant.Roles);
            var token = await _tokens.CreateAccessTokenAsync(user.UserId, tenant.TenantId, claims, ct);
            return AuthResultResponse.WithToken(token, createdNewUser);
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
                return AuthResultResponse.WithToken(token, createdNewUser);
            }
        }

        var preClaims = BuildBaseClaims(user);
        preClaims.Add(new ClaimItem("pt", "1"));
        var preToken = await _tokens.CreatePreTenantTokenAsync(user.UserId, preClaims, ct);
        return AuthResultResponse.RequiresSelection(preToken.AccessToken, activeTenants, createdNewUser);
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

    private async Task InvokeLifecycleAndPublishAsync<TEvent>(
        TEvent evt,
        Func<TEvent, Task> hookInvoker,
        CancellationToken ct)
        where TEvent : AuthLifecycleEventBase
    {
        try
        {
            await hookInvoker(evt);
            await _eventPublisher.PublishAsync(evt, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to emit auth lifecycle event {EventType}. CorrelationId={CorrelationId}, EventId={EventId}",
                evt.EventType,
                evt.CorrelationId ?? "n/a",
                evt.EventId);

            if (_eventOptions.Value.StrictPublishFailures)
                throw;
        }
    }

    private static string? ResolveTraceId()
    {
        var current = Activity.Current;
        if (current is null)
            return null;

        return current.TraceId != default
            ? current.TraceId.ToString()
            : current.Id;
    }
}
