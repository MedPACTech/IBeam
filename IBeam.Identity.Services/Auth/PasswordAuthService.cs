using IBeam.Identity.Exceptions;
using IBeam.Identity.Events;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Tenants;
using IBeam.Identity.Services.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace IBeam.Identity.Services.Auth;

public sealed class PasswordAuthService : IIdentityAuthService
{
    private readonly IIdentityUserStore _users;
    private readonly ITenantMembershipStore _tenants;
    private readonly ITenantProvisioningService _tenantProvisioning;
    private readonly ITokenService _tokens;
    private readonly IOtpService _otpService;
    private readonly IOtpChallengeStore _otpChallenges;
    private readonly IIdentityCommunicationSender _sender;
    private readonly IAuthAttemptStore _attempts;
    private readonly IOptions<LoginAttemptOptions> _attemptOptions;
    private readonly IOptions<TenantProvisioningOptions> _tenantProvisioningOptions;
    private readonly IAuthEventPublisher _eventPublisher;
    private readonly IAuthLifecycleHook _lifecycleHook;
    private readonly IOptions<AuthEventOptions> _eventOptions;
    private readonly ITenantInfoResolver _tenantInfoResolver;
    private readonly ITenantExtensionCoordinator _tenantExtensions;
    private readonly ILogger<PasswordAuthService> _logger;

    public PasswordAuthService(
        IIdentityUserStore users,
        ITenantMembershipStore tenants,
        ITenantProvisioningService tenantProvisioning,
        ITokenService tokens,
        IOtpService otpService,
        IOtpChallengeStore otpChallenges,
        IIdentityCommunicationSender sender,
        IAuthAttemptStore attempts,
        IOptions<LoginAttemptOptions> attemptOptions,
        IOptions<TenantProvisioningOptions> tenantProvisioningOptions,
        IAuthEventPublisher eventPublisher,
        IAuthLifecycleHook lifecycleHook,
        IOptions<AuthEventOptions> eventOptions,
        ILogger<PasswordAuthService> logger)
        : this(
            users,
            tenants,
            tenantProvisioning,
            tokens,
            otpService,
            otpChallenges,
            sender,
            attempts,
            attemptOptions,
            tenantProvisioningOptions,
            eventPublisher,
            lifecycleHook,
            eventOptions,
            new TenantInfoResolver(new NoOpTenantMetadataProvider()),
            new NoOpTenantExtensionCoordinator(),
            logger)
    {
    }

    public PasswordAuthService(
        IIdentityUserStore users,
        ITenantMembershipStore tenants,
        ITenantProvisioningService tenantProvisioning,
        ITokenService tokens,
        IOtpService otpService,
        IOtpChallengeStore otpChallenges,
        IIdentityCommunicationSender sender,
        IAuthAttemptStore attempts,
        IOptions<LoginAttemptOptions> attemptOptions,
        IOptions<TenantProvisioningOptions> tenantProvisioningOptions,
        IAuthEventPublisher eventPublisher,
        IAuthLifecycleHook lifecycleHook,
        IOptions<AuthEventOptions> eventOptions,
        ITenantInfoResolver tenantInfoResolver,
        ITenantExtensionCoordinator tenantExtensions,
        ILogger<PasswordAuthService> logger)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _tenantProvisioning = tenantProvisioning ?? throw new ArgumentNullException(nameof(tenantProvisioning));
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _otpService = otpService ?? throw new ArgumentNullException(nameof(otpService));
        _otpChallenges = otpChallenges ?? throw new ArgumentNullException(nameof(otpChallenges));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _attempts = attempts ?? throw new ArgumentNullException(nameof(attempts));
        _attemptOptions = attemptOptions ?? throw new ArgumentNullException(nameof(attemptOptions));
        _tenantProvisioningOptions = tenantProvisioningOptions ?? throw new ArgumentNullException(nameof(tenantProvisioningOptions));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _lifecycleHook = lifecycleHook ?? throw new ArgumentNullException(nameof(lifecycleHook));
        _eventOptions = eventOptions ?? throw new ArgumentNullException(nameof(eventOptions));
        _tenantInfoResolver = tenantInfoResolver ?? throw new ArgumentNullException(nameof(tenantInfoResolver));
        _tenantExtensions = tenantExtensions ?? throw new ArgumentNullException(nameof(tenantExtensions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public PasswordAuthService(
        IIdentityUserStore users,
        ITenantMembershipStore tenants,
        ITenantProvisioningService tenantProvisioning,
        ITokenService tokens,
        IOtpService otpService,
        IOtpChallengeStore otpChallenges,
        IIdentityCommunicationSender sender)
        : this(
            users,
            tenants,
            tenantProvisioning,
            tokens,
            otpService,
            otpChallenges,
            sender,
            new Auth.Attempts.InMemoryAuthAttemptStore(),
            Microsoft.Extensions.Options.Options.Create(new LoginAttemptOptions()),
            Microsoft.Extensions.Options.Options.Create(new TenantProvisioningOptions()),
            new NoOpAuthEventPublisher(),
            new NoOpAuthLifecycleHook(),
            Microsoft.Extensions.Options.Options.Create(new AuthEventOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PasswordAuthService>.Instance)
    {
    }

    public async Task RegisterAsync(RegisterUserRequest request, CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.Email)) throw new IdentityValidationException("Email is required.");
        if (string.IsNullOrWhiteSpace(request.Password)) throw new IdentityValidationException("Password is required.");

        var traceId = ResolveTraceId();
        var pre = new AuthUserCreateRequestedEvent
        {
            NormalizedEmail = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant(),
            NormalizedPhone = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
            TraceId = traceId
        };
        pre.Metadata["idempotencyKey"] =
            $"{AuthUserCreateRequestedEvent.TypeName}:{pre.NormalizedEmail ?? pre.NormalizedPhone ?? Guid.NewGuid().ToString("N")}";
        await InvokeLifecycleAndPublishAsync(pre, (hook, evt, token) => hook.OnBeforeAuthUserCreateAsync(evt, token), ct);

        var result = await _users.CreateAsync(request, ct);
        if (!result.Succeeded)
            throw new IdentityValidationException("Registration failed.", result.Errors);
        if (result.User is null)
            throw new IdentityProviderException("UnknownProvider", "User store returned success but no user.");

        var created = new AuthUserCreatedEvent
        {
            AuthUserId = result.User.UserId.ToString("D"),
            NormalizedEmail = string.IsNullOrWhiteSpace(result.User.Email) ? null : result.User.Email.Trim().ToLowerInvariant(),
            NormalizedPhone = string.IsNullOrWhiteSpace(result.User.PhoneNumber) ? null : result.User.PhoneNumber.Trim(),
            TraceId = traceId
        };
        created.Metadata["idempotencyKey"] = $"{AuthUserCreatedEvent.TypeName}:{result.User.UserId:D}";
        await InvokeLifecycleAndPublishAsync(created, (hook, evt, token) => hook.OnAuthUserCreatedAsync(evt, token), ct);
    }

    public async Task<AuthResultResponse> PasswordLoginAsync(PasswordLoginRequest request, CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.Email)) throw new IdentityValidationException("Email is required.");
        if (string.IsNullOrWhiteSpace(request.Password)) throw new IdentityValidationException("Password is required.");

        var traceId = ResolveTraceId();
        var normalizedIdentifier = request.Email.Trim().ToLowerInvariant();

        await EnsureNotLockedAsync("password", normalizedIdentifier, ct);
        var loginAttempt = new LoginAttemptedEvent
        {
            Method = "password",
            Identifier = normalizedIdentifier,
            TraceId = traceId
        };
        loginAttempt.Metadata["idempotencyKey"] = $"{LoginAttemptedEvent.TypeName}:password:{loginAttempt.Identifier}";
        await InvokeLifecycleAndPublishAsync(loginAttempt, (hook, evt, token) => hook.OnBeforeLoginAsync(evt, token), ct);

        var user = await _users.FindByEmailAsync(request.Email, ct);
        if (user is null)
        {
            await RegisterFailedAttemptAsync("password", normalizedIdentifier, ct);
            await EmitLoginFailedAsync("password", request.Email, "User not found.", null, traceId, ct);
            throw new IdentityUnauthorizedException("Invalid credentials.");
        }

        var ok = await _users.ValidatePasswordAsync(request.Email, request.Password, ct);
        if (!ok)
        {
            await RegisterFailedAttemptAsync("password", normalizedIdentifier, ct);
            await EmitLoginFailedAsync("password", request.Email, "Password invalid.", null, traceId, ct);
            throw new IdentityUnauthorizedException("Invalid credentials.");
        }

        await _attempts.RegisterSuccessAsync("password", normalizedIdentifier, ct);

        if (user.TwoFactorEnabled)
        {
            var (method, channel, destination) = ResolveTwoFactorTarget(user, user.PreferredTwoFactorMethod);
            var challengeRequested = new OtpChallengeRequestedEvent
            {
                Destination = destination,
                Purpose = SenderPurpose.LoginMfa.ToString(),
                TraceId = traceId
            };
            challengeRequested.Metadata["idempotencyKey"] =
                $"{OtpChallengeRequestedEvent.TypeName}:{SenderPurpose.LoginMfa}:{destination}";
            await InvokeLifecycleAndPublishAsync(
                challengeRequested,
                (hook, evt, token) => hook.OnBeforeOtpChallengeCreateAsync(evt, token),
                ct);

            var challenge = await _otpService.CreateChallengeAsync(
                new OtpChallengeRequest(channel, destination, SenderPurpose.LoginMfa, null),
                ct);
            var challengeEvent = new OtpChallengeCreatedEvent
            {
                ChallengeId = challenge.ChallengeId,
                Destination = destination,
                Purpose = SenderPurpose.LoginMfa.ToString(),
                TraceId = traceId
            };
            challengeEvent.Metadata["idempotencyKey"] = $"{OtpChallengeCreatedEvent.TypeName}:{challenge.ChallengeId}";
            await InvokeLifecycleAndPublishAsync(challengeEvent, (hook, evt, token) => hook.OnOtpChallengeCreatedAsync(evt, token), ct);

            return AuthResultResponse.RequiresTwoFactorChallenge(challenge.ChallengeId, method);
        }

        var activeTenants = await GetActiveTenantsForUserAsync(user.UserId, ct);
        activeTenants = await ApplyTenantProvisioningPolicyAsync(user, createdNewUser: false, activeTenants, traceId, ct);
        await EnsureTenantExtensionsForUserAsync(user.UserId, TenantExtensionOperations.Ensure, null, null, traceId, ct);

        var policyTenantId = ResolveEffectiveTenantId();
        if (policyTenantId.HasValue)
        {
            var tenant = activeTenants.FirstOrDefault(t => t.TenantId == policyTenantId.Value)
                ?? throw new IdentityValidationException($"User is not linked to tenant '{policyTenantId.Value:D}'.");

            var claims = BuildBaseClaims(user.UserId, user.Email);
            AddTenantClaims(claims, tenant.TenantId);
            AddRoleClaims(claims, tenant.Roles);
            AddRoleIdClaims(claims, tenant.RoleIds);
            var token = await _tokens.CreateAccessTokenAsync(user.UserId, tenant.TenantId, claims, ct);
            await EmitLoginSucceededAsync("password", user.UserId, tenant.TenantId, false, traceId, ct);
            return AuthResultResponse.WithToken(token);
        }

        if (activeTenants.Count == 0)
            throw new IdentityUnauthorizedException("No active tenant membership.");

        if (activeTenants.Count == 1)
        {
            var t = activeTenants[0];
            var claims = BuildBaseClaims(user.UserId, user.Email);
            AddTenantClaims(claims, t.TenantId);
            AddRoleClaims(claims, t.Roles);
            AddRoleIdClaims(claims, t.RoleIds);
            var token = await _tokens.CreateAccessTokenAsync(user.UserId, t.TenantId, claims, ct);
            await EmitLoginSucceededAsync("password", user.UserId, t.TenantId, false, traceId, ct);
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
                AddRoleIdClaims(claims, def.RoleIds);
                var token = await _tokens.CreateAccessTokenAsync(user.UserId, def.TenantId, claims, ct);
                await EmitLoginSucceededAsync("password", user.UserId, def.TenantId, false, traceId, ct);
                return AuthResultResponse.WithToken(token);
            }
        }

        var preClaims = BuildBaseClaims(user.UserId, user.Email);
        preClaims.Add(new ClaimItem("pt", "1"));
        var pre = await _tokens.CreatePreTenantTokenAsync(user.UserId, preClaims, ct);
        await EmitLoginSucceededAsync("password", user.UserId, null, true, traceId, ct);
        return AuthResultResponse.RequiresSelection(pre.AccessToken, activeTenants);
    }

    public async Task<OtpChallengeResult> StartTwoFactorSetupAsync(Guid userId, string method, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
            throw new IdentityValidationException("UserId is required.");

        var user = await _users.FindByIdAsync(userId, ct)
            ?? throw new IdentityValidationException("User not found.");

        var (_, channel, destination) = ResolveTwoFactorTarget(user, method);
        var purpose = channel == SenderChannel.Email ? SenderPurpose.EmailVerification : SenderPurpose.PhoneVerification;
        var traceId = ResolveTraceId();
        var requested = new OtpChallengeRequestedEvent
        {
            Destination = destination,
            Purpose = purpose.ToString(),
            TraceId = traceId
        };
        requested.Metadata["idempotencyKey"] = $"{OtpChallengeRequestedEvent.TypeName}:{purpose}:{destination}";
        await InvokeLifecycleAndPublishAsync(
            requested,
            (hook, evt, token) => hook.OnBeforeOtpChallengeCreateAsync(evt, token),
            ct);

        return await _otpService.CreateChallengeAsync(
            new OtpChallengeRequest(channel, destination, purpose, null),
            ct);
    }

    public async Task CompleteTwoFactorSetupAsync(Guid userId, string method, string challengeId, string code, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
            throw new IdentityValidationException("UserId is required.");
        if (string.IsNullOrWhiteSpace(challengeId))
            throw new IdentityValidationException("ChallengeId is required.");
        if (string.IsNullOrWhiteSpace(code))
            throw new IdentityValidationException("Code is required.");

        var user = await _users.FindByIdAsync(userId, ct)
            ?? throw new IdentityValidationException("User not found.");

        var (normalizedMethod, channel, destination) = ResolveTwoFactorTarget(user, method);
        var expectedPurpose = channel == SenderChannel.Email ? SenderPurpose.EmailVerification : SenderPurpose.PhoneVerification;

        await VerifyOtpChallengeAsync(challengeId, code, destination, expectedPurpose, ct);
        if (channel == SenderChannel.Email)
            await _users.SetEmailConfirmedAsync(user.UserId, true, ct);
        else
            await _users.SetPhoneConfirmedAsync(user.UserId, true, ct);

        await _users.SetTwoFactorAsync(user.UserId, enabled: true, preferredMethod: normalizedMethod, ct);
    }

    public async Task<AuthResultResponse> CompleteTwoFactorLoginAsync(string email, string challengeId, string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new IdentityValidationException("Email is required.");
        if (string.IsNullOrWhiteSpace(challengeId))
            throw new IdentityValidationException("ChallengeId is required.");
        if (string.IsNullOrWhiteSpace(code))
            throw new IdentityValidationException("Code is required.");

        var user = await _users.FindByEmailAsync(email, ct);
        if (user is null)
            throw new IdentityUnauthorizedException("Invalid credentials.");
        if (!user.TwoFactorEnabled)
            throw new IdentityValidationException("Two-factor authentication is not enabled for this user.");

        var (_, _, destination) = ResolveTwoFactorTarget(user, user.PreferredTwoFactorMethod);
        await VerifyOtpChallengeAsync(challengeId, code, destination, SenderPurpose.LoginMfa, ct);

        return await BuildAuthResultAsync(user, createdNewUser: false, ct);
    }

    public async Task DisableTwoFactorAsync(Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
            throw new IdentityValidationException("UserId is required.");

        var user = await _users.FindByIdAsync(userId, ct)
            ?? throw new IdentityValidationException("User not found.");

        await _users.SetTwoFactorAsync(user.UserId, enabled: false, preferredMethod: null, ct);
    }

    public async Task SetPreferredTwoFactorMethodAsync(Guid userId, string method, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
            throw new IdentityValidationException("UserId is required.");

        var user = await _users.FindByIdAsync(userId, ct)
            ?? throw new IdentityValidationException("User not found.");

        if (!user.TwoFactorEnabled)
            throw new IdentityValidationException("Two-factor authentication is not enabled for this user.");

        var (normalizedMethod, _, _) = ResolveTwoFactorTarget(user, method);
        await _users.SetTwoFactorAsync(user.UserId, enabled: true, preferredMethod: normalizedMethod, ct);
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
            VerificationTokenExpiresAt: expiresAt,
            Channel: SenderChannel.Email);

        await _otpChallenges.SaveAsync(challenge, ct);

        var link = BuildResetLink(resetUrlBase, challengeId, verificationToken, normalizedEmail, displayName);
        await _sender.SendAsync(new IdentitySenderMessage
        {
            Channel = SenderChannel.Email,
            Destination = normalizedEmail,
            Purpose = SenderPurpose.UserRegistration,
            Subject = "Verify your email",
            Body = $"Click this link to finish account setup: {link}",
            ExpiresAt = expiresAt,
            Metadata = new Dictionary<string, object>
            {
                ["Link"] = link,
                ["DisplayName"] = displayName ?? string.Empty,
                ["ChallengeId"] = challengeId
            }
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
            var traceId = ResolveTraceId();
            var preCreate = new AuthUserCreateRequestedEvent
            {
                NormalizedEmail = normalizedEmail,
                TraceId = traceId,
                CorrelationId = challengeId,
                CausationId = challengeId
            };
            preCreate.Metadata["idempotencyKey"] = $"{AuthUserCreateRequestedEvent.TypeName}:{normalizedEmail}";
            await InvokeLifecycleAndPublishAsync(preCreate, (hook, evt, token) => hook.OnBeforeAuthUserCreateAsync(evt, token), ct);

            var createResult = await _users.CreateAsync(
                new RegisterUserRequest(normalizedEmail, null, string.Empty, displayName),
                ct);

            if (!createResult.Succeeded || createResult.User is null)
                throw new IdentityValidationException("User creation failed.", createResult.Errors);

            user = createResult.User;
            createdNewUser = true;

            var created = new AuthUserCreatedEvent
            {
                AuthUserId = user.UserId.ToString("D"),
                NormalizedEmail = normalizedEmail,
                CorrelationId = challengeId,
                CausationId = challengeId,
                TraceId = traceId
            };
            created.Metadata["idempotencyKey"] = $"{AuthUserCreatedEvent.TypeName}:{user.UserId:D}";
            await InvokeLifecycleAndPublishAsync(created, (hook, evt, token) => hook.OnAuthUserCreatedAsync(evt, token), ct);
        }

        await _users.SetPasswordAsync(user.UserId, newPassword, ct);
        await _users.SetEmailConfirmedAsync(user.UserId, true, ct);

        return await BuildAuthResultAsync(user, createdNewUser, ct);
    }

    public async Task<RequestPasswordResetResponse> StartEmailPasswordLinkAsync(
        Guid userId,
        string email,
        string? displayName = null,
        string? resetUrlBase = null,
        CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
            throw new IdentityValidationException("UserId is required.");
        if (string.IsNullOrWhiteSpace(email))
            throw new IdentityValidationException("Email is required.");

        var user = await _users.FindByIdAsync(userId, ct)
            ?? throw new IdentityValidationException("User not found.");

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var existing = await _users.FindByEmailAsync(normalizedEmail, ct);
        if (existing is not null && existing.UserId != user.UserId)
            throw new IdentityValidationException("Email is already bound to another user.");

        return await StartEmailPasswordRegistrationAsync(normalizedEmail, displayName ?? user.DisplayName, resetUrlBase, ct);
    }

    public async Task CompleteEmailPasswordLinkAsync(
        Guid userId,
        string email,
        string challengeId,
        string verificationToken,
        string newPassword,
        CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
            throw new IdentityValidationException("UserId is required.");
        if (string.IsNullOrWhiteSpace(email))
            throw new IdentityValidationException("Email is required.");
        if (string.IsNullOrWhiteSpace(challengeId))
            throw new IdentityValidationException("ChallengeId is required.");
        if (string.IsNullOrWhiteSpace(verificationToken))
            throw new IdentityValidationException("Verification token is required.");
        if (string.IsNullOrWhiteSpace(newPassword))
            throw new IdentityValidationException("New password is required.");

        var user = await _users.FindByIdAsync(userId, ct)
            ?? throw new IdentityValidationException("User not found.");

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var existing = await _users.FindByEmailAsync(normalizedEmail, ct);
        if (existing is not null && existing.UserId != user.UserId)
            throw new IdentityValidationException("Email is already bound to another user.");

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

        await _users.UpdateEmailAsync(user.UserId, normalizedEmail, ct);
        await _users.SetPasswordAsync(user.UserId, newPassword, ct);
        await _users.SetEmailConfirmedAsync(user.UserId, true, ct);
    }

    public async Task<OtpChallengeResult> StartPhoneLinkAsync(Guid userId, string phoneNumber, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
            throw new IdentityValidationException("UserId is required.");
        if (string.IsNullOrWhiteSpace(phoneNumber))
            throw new IdentityValidationException("Phone number is required.");

        var user = await _users.FindByIdAsync(userId, ct)
            ?? throw new IdentityValidationException("User not found.");

        var (channel, normalizedPhone) = IdentityUtils.NormalizeDestination(phoneNumber);
        if (channel != SenderChannel.Sms)
            throw new IdentityValidationException("Destination must be a valid phone number.");

        var existing = await _users.FindByPhoneAsync(normalizedPhone, ct);
        if (existing is not null && existing.UserId != user.UserId)
            throw new IdentityValidationException("Phone number is already bound to another user.");

        return await _otpService.CreateChallengeAsync(
            new OtpChallengeRequest(SenderChannel.Sms, normalizedPhone, SenderPurpose.PhoneVerification, null),
            ct);
    }

    public async Task CompletePhoneLinkAsync(
        Guid userId,
        string phoneNumber,
        string challengeId,
        string code,
        CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
            throw new IdentityValidationException("UserId is required.");
        if (string.IsNullOrWhiteSpace(phoneNumber))
            throw new IdentityValidationException("Phone number is required.");
        if (string.IsNullOrWhiteSpace(challengeId))
            throw new IdentityValidationException("ChallengeId is required.");
        if (string.IsNullOrWhiteSpace(code))
            throw new IdentityValidationException("Code is required.");

        var user = await _users.FindByIdAsync(userId, ct)
            ?? throw new IdentityValidationException("User not found.");

        var (channel, normalizedPhone) = IdentityUtils.NormalizeDestination(phoneNumber);
        if (channel != SenderChannel.Sms)
            throw new IdentityValidationException("Destination must be a valid phone number.");

        var existing = await _users.FindByPhoneAsync(normalizedPhone, ct);
        if (existing is not null && existing.UserId != user.UserId)
            throw new IdentityValidationException("Phone number is already bound to another user.");

        await VerifyOtpChallengeAsync(challengeId, code, normalizedPhone, SenderPurpose.PhoneVerification, ct);
        await _users.UpdatePhoneAsync(user.UserId, normalizedPhone, ct);
        await _users.SetPhoneConfirmedAsync(user.UserId, true, ct);
    }

    public async Task<AuthTokenResponse> SelectTenantAsync(string userId, SelectTenantRequest request, CancellationToken ct = default)
        => await SelectTenantInternalAsync(userId, request, "select", ct);

    public async Task<AuthTokenResponse> SwitchTenantAsync(string userId, SelectTenantRequest request, CancellationToken ct = default)
        => await SelectTenantInternalAsync(userId, request, "switch", ct);

    private async Task<AuthTokenResponse> SelectTenantInternalAsync(
        string userId,
        SelectTenantRequest request,
        string operation,
        CancellationToken ct)
    {
        var userGuid = ParseUserId(userId);
        if (request is null) throw new ArgumentNullException(nameof(request));
        var traceId = ResolveTraceId();

        var pre = new TenantSelectionRequestedEvent
        {
            AuthUserId = userGuid.ToString("D"),
            TenantId = request.TenantId,
            SetAsDefault = request.SetAsDefault,
            Operation = operation,
            CorrelationId = request.TenantId.ToString("D"),
            CausationId = request.TenantId.ToString("D"),
            TraceId = traceId
        };
        pre.Metadata["idempotencyKey"] =
            $"{TenantSelectionRequestedEvent.TypeName}:{operation}:{request.TenantId:D}:{userGuid:D}";
        await InvokeLifecycleAndPublishAsync(
            pre,
            (hook, evt, token) => hook.OnBeforeTenantSelectionAsync(evt, token),
            ct);

        var tenant = await _tenantInfoResolver
            .EnrichAsync(await _tenants.GetTenantForUserAsync(userGuid, request.TenantId, ct).ConfigureAwait(false), ct)
            .ConfigureAwait(false);
        if (tenant is null || !tenant.IsActive)
            throw new IdentityUnauthorizedException("No active tenant membership.");
        if (request.SetAsDefault)
            await _tenants.SetDefaultTenantAsync(userGuid, request.TenantId, ct);
        await _tenantExtensions.EnsureExtensionAsync(
            IdentityTenant.FromTenantInfo(tenant),
            TenantExtensionContext.Create(
                TenantExtensionOperations.Selected,
                userGuid,
                request.TenantId.ToString("D"),
                request.TenantId.ToString("D"),
                traceId),
            ct).ConfigureAwait(false);
        var claims = BuildBaseClaims(userGuid, string.Empty);
        AddTenantClaims(claims, tenant.TenantId);
        AddRoleClaims(claims, tenant.Roles);
        AddRoleIdClaims(claims, tenant.RoleIds);
        var token = await _tokens.CreateAccessTokenAsync(userGuid, tenant.TenantId, claims, ct);

        var selected = new TenantSelectedEvent
        {
            AuthUserId = userGuid.ToString("D"),
            TenantId = tenant.TenantId,
            SetAsDefault = request.SetAsDefault,
            Operation = operation,
            CorrelationId = request.TenantId.ToString("D"),
            CausationId = request.TenantId.ToString("D"),
            TraceId = traceId
        };
        selected.Metadata["idempotencyKey"] =
            $"{TenantSelectedEvent.TypeName}:{operation}:{tenant.TenantId:D}:{userGuid:D}";
        await InvokeLifecycleAndPublishAsync(
            selected,
            (hook, evt, token) => hook.OnTenantSelectedAsync(evt, token),
            ct);

        return ToAuthTokenResponse(token);
    }

    private async Task<AuthResultResponse> BuildAuthResultAsync(IdentityUser user, bool createdNewUser, CancellationToken ct)
    {
        var traceId = ResolveTraceId();
        var activeTenants = await GetActiveTenantsForUserAsync(user.UserId, ct);

        activeTenants = await ApplyTenantProvisioningPolicyAsync(user, createdNewUser, activeTenants, traceId, ct);
        await EnsureTenantExtensionsForUserAsync(user.UserId, TenantExtensionOperations.Ensure, null, null, traceId, ct);

        var policyTenantId = ResolveEffectiveTenantId();
        if (policyTenantId.HasValue)
        {
            var requestedTenant = activeTenants.FirstOrDefault(t => t.TenantId == policyTenantId.Value)
                ?? throw new IdentityValidationException($"User is not linked to tenant '{policyTenantId.Value:D}'.");

            activeTenants = new List<TenantInfo> { requestedTenant };
        }

        if (activeTenants.Count == 1)
        {
            var tenant = activeTenants[0];
            var claims = BuildBaseClaims(user.UserId, user.Email);
            AddTenantClaims(claims, tenant.TenantId);
            AddRoleClaims(claims, tenant.Roles);
            AddRoleIdClaims(claims, tenant.RoleIds);
            var token = await _tokens.CreateAccessTokenAsync(user.UserId, tenant.TenantId, claims, ct);
            await EmitLoginSucceededAsync("password", user.UserId, tenant.TenantId, false, traceId, ct);
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
                AddRoleIdClaims(claims, def.RoleIds);
                var token = await _tokens.CreateAccessTokenAsync(user.UserId, def.TenantId, claims, ct);
                await EmitLoginSucceededAsync("password", user.UserId, def.TenantId, false, traceId, ct);
                return AuthResultResponse.WithToken(token, createdNewUser);
            }
        }

        var preClaims = BuildBaseClaims(user.UserId, user.Email);
        preClaims.Add(new ClaimItem("pt", "1"));
        var pre = await _tokens.CreatePreTenantTokenAsync(user.UserId, preClaims, ct);
        await EmitLoginSucceededAsync("password", user.UserId, null, true, traceId, ct);
        return AuthResultResponse.RequiresSelection(pre.AccessToken, activeTenants, createdNewUser);
    }

    private async Task<List<TenantInfo>> ApplyTenantProvisioningPolicyAsync(
        IdentityUser user,
        bool createdNewUser,
        List<TenantInfo> activeTenants,
        string? traceId,
        CancellationToken ct)
    {
        var options = _tenantProvisioningOptions.Value;
        var configuredTenantId = ResolveEffectiveTenantId();

        if (options.Mode == TenantProvisioningMode.RequireExistingTenant)
        {
            if (configuredTenantId.HasValue)
            {
                if (activeTenants.Any(t => t.TenantId == configuredTenantId.Value))
                    return activeTenants;

                throw new IdentityValidationException($"User is not linked to required tenant '{configuredTenantId.Value:D}'.");
            }

            if (activeTenants.Count == 0)
                throw new IdentityValidationException("User does not have an active tenant membership.");

            return activeTenants;
        }

        if (options.Mode == TenantProvisioningMode.UseDefaultTenant)
        {
            var defaultTenantId = configuredTenantId ?? throw new IdentityValidationException("Default tenant id is required.");
            if (activeTenants.Any(t => t.TenantId == defaultTenantId))
                return activeTenants;

            if (!options.AutoLinkUserToDefaultTenant)
                throw new IdentityValidationException($"User is not linked to default tenant '{defaultTenantId:D}'.");

            await _tenantProvisioning.EnsureUserTenantMembershipAsync(
                defaultTenantId,
                user.UserId,
                roleNames: options.AutoLinkRoleNames,
                setAsDefault: true,
                ct: ct);

            activeTenants = await GetActiveTenantsForUserAsync(user.UserId, ct);

            if (!activeTenants.Any(t => t.TenantId == defaultTenantId))
                throw new IdentityProviderException("Default tenant membership could not be resolved after linking.");

            return activeTenants;
        }

        if (createdNewUser || activeTenants.Count == 0)
        {
            var preTenant = new TenantCreateRequestedEvent
            {
                AuthUserId = user.UserId.ToString("D"),
                SuggestedTenantName = string.IsNullOrWhiteSpace(user.Email) ? null : $"{user.Email.Split('@')[0]}'s Workspace",
                TraceId = traceId
            };
            preTenant.Metadata["idempotencyKey"] = $"{TenantCreateRequestedEvent.TypeName}:{user.UserId:D}";
            await InvokeLifecycleAndPublishAsync(preTenant, (hook, evt, token) => hook.OnBeforeTenantCreateAsync(evt, token), ct);

            var createdTenantId = await _tenantProvisioning.CreateTenantForNewUserAsync(user.UserId, user.Email, ct);
            activeTenants = await GetActiveTenantsForUserAsync(user.UserId, ct);

            if (!activeTenants.Any(t => t.TenantId == createdTenantId))
                throw new IdentityProviderException("Tenant provisioning completed but membership could not be resolved.");

            var createdTenant = activeTenants.FirstOrDefault(t => t.TenantId == createdTenantId);
            var tenantCreated = new TenantCreatedEvent
            {
                TenantId = createdTenantId,
                TenantName = createdTenant?.Name,
                Status = IdentityTenantStatuses.Active,
                TraceId = traceId
            };
            tenantCreated.Metadata["idempotencyKey"] = $"{TenantCreatedEvent.TypeName}:{createdTenantId:D}";
            await InvokeLifecycleAndPublishAsync(tenantCreated, (hook, evt, token) => hook.OnTenantCreatedAsync(evt, token), ct);

            if (createdTenant is not null)
            {
                await _tenantExtensions.OnTenantCreatedAsync(
                    IdentityTenant.FromTenantInfo(createdTenant, DateTimeOffset.UtcNow),
                    TenantExtensionContext.Create(
                        TenantExtensionOperations.Created,
                        user.UserId,
                        traceId: traceId,
                        metadata: tenantCreated.Metadata),
                    ct).ConfigureAwait(false);
            }

            var preLink = new TenantUserLinkRequestedEvent
            {
                TenantId = createdTenantId,
                AuthUserId = user.UserId.ToString("D"),
                TraceId = traceId
            };
            preLink.Metadata["idempotencyKey"] = $"{TenantUserLinkRequestedEvent.TypeName}:{createdTenantId:D}:{user.UserId:D}";
            await InvokeLifecycleAndPublishAsync(preLink, (hook, evt, token) => hook.OnBeforeTenantUserLinkAsync(evt, token), ct);

            var linked = new TenantUserLinkedEvent
            {
                TenantId = createdTenantId,
                AuthUserId = user.UserId.ToString("D"),
                Role = createdTenant?.Roles?.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r)),
                UserTenantId = $"{createdTenantId:D}|{user.UserId:D}",
                TraceId = traceId
            };
            linked.Metadata["idempotencyKey"] = $"{TenantUserLinkedEvent.TypeName}:{createdTenantId:D}:{user.UserId:D}";
            await InvokeLifecycleAndPublishAsync(linked, (hook, evt, token) => hook.OnTenantUserLinkedAsync(evt, token), ct);
        }

        return activeTenants;
    }

    private async Task<List<TenantInfo>> GetActiveTenantsForUserAsync(Guid userId, CancellationToken ct)
    {
        var tenants = await _tenantInfoResolver
            .EnrichAsync(await _tenants.GetTenantsForUserAsync(userId, ct).ConfigureAwait(false), ct)
            .ConfigureAwait(false);

        return tenants.Where(t => t.IsActive).ToList();
    }

    private async Task EnsureTenantExtensionsForUserAsync(
        Guid userId,
        string operation,
        string? correlationId,
        string? causationId,
        string? traceId,
        CancellationToken ct)
    {
        var identityTenantsTask = _tenants.GetTenantsForUserAsync(userId, ct);
        if (identityTenantsTask is null)
            return;

        var identityTenants = await identityTenantsTask.ConfigureAwait(false);
        if (identityTenants is null)
            return;

        foreach (var tenant in identityTenants.Where(t => t.IsActive))
        {
            await _tenantExtensions.EnsureExtensionAsync(
                IdentityTenant.FromTenantInfo(tenant),
                TenantExtensionContext.Create(operation, userId, correlationId, causationId, traceId),
                ct).ConfigureAwait(false);
        }
    }

    private Guid? ResolveEffectiveTenantId()
    {
        var options = _tenantProvisioningOptions.Value;
        return options.Mode == TenantProvisioningMode.UseDefaultTenant ||
               (options.Mode == TenantProvisioningMode.RequireExistingTenant && options.DefaultTenantId.HasValue)
            ? options.DefaultTenantId
            : null;
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

    private async Task VerifyOtpChallengeAsync(
        string challengeId,
        string code,
        string expectedDestination,
        SenderPurpose expectedPurpose,
        CancellationToken ct)
    {
        var verifyRequested = new OtpVerifyRequestedEvent
        {
            ChallengeId = challengeId,
            Destination = expectedDestination,
            Purpose = expectedPurpose.ToString(),
            CorrelationId = challengeId,
            CausationId = challengeId,
            TraceId = ResolveTraceId()
        };
        verifyRequested.Metadata["idempotencyKey"] = $"{OtpVerifyRequestedEvent.TypeName}:{challengeId}";
        await InvokeLifecycleAndPublishAsync(
            verifyRequested,
            (hook, evt, token) => hook.OnBeforeOtpVerifyAsync(evt, token),
            ct);

        var verify = await _otpService.VerifyAsync(new OtpVerifyRequest(challengeId, code), ct);
        if (!verify.Success)
            throw new IdentityValidationException("OTP verification failed.");

        var challenge = await _otpChallenges.GetAsync(challengeId, ct);
        if (challenge is null)
            throw new IdentityValidationException("OTP challenge not found.");
        if (!challenge.IsConsumed)
            throw new IdentityValidationException("OTP challenge was not consumed.");
        if (challenge.Purpose != expectedPurpose)
            throw new IdentityValidationException("OTP challenge purpose mismatch.");

        var (_, normalizedExpected) = IdentityUtils.NormalizeDestination(expectedDestination);
        var (_, normalizedActual) = IdentityUtils.NormalizeDestination(challenge.Destination);
        if (!string.Equals(normalizedActual, normalizedExpected, StringComparison.OrdinalIgnoreCase))
            throw new IdentityValidationException("OTP destination mismatch.");
    }

    private static (string Method, SenderChannel Channel, string Destination) ResolveTwoFactorTarget(IdentityUser user, string? requestedMethod)
    {
        var method = (requestedMethod ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(method))
            method = !string.IsNullOrWhiteSpace(user.Email) ? "email" : "sms";

        if (method == "email")
        {
            if (string.IsNullOrWhiteSpace(user.Email))
                throw new IdentityValidationException("User does not have an email for 2FA.");
            return (method, SenderChannel.Email, user.Email);
        }

        if (method == "sms")
        {
            if (string.IsNullOrWhiteSpace(user.PhoneNumber))
                throw new IdentityValidationException("User does not have a phone number for SMS 2FA.");
            return (method, SenderChannel.Sms, user.PhoneNumber);
        }

        throw new IdentityValidationException("2FA method must be 'email' or 'sms'.");
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

    private static void AddRoleIdClaims(List<ClaimItem> claims, IEnumerable<Guid>? roleIds)
    {
        if (roleIds is null) return;
        foreach (var roleId in roleIds.Where(x => x != Guid.Empty).Distinct())
            claims.Add(new("rid", roleId.ToString("D")));
    }

    private async Task InvokeLifecycleAndPublishAsync<TEvent>(
        TEvent evt,
        Func<IAuthLifecycleHook, TEvent, CancellationToken, Task> hookInvoker,
        CancellationToken ct)
        where TEvent : AuthLifecycleEventBase
    {
        try
        {
            await hookInvoker(_lifecycleHook, evt, ct);
            await _eventPublisher.PublishAsync(evt, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to emit auth lifecycle event {EventType}. EventId={EventId}", evt.EventType, evt.EventId);
            if (_eventOptions.Value.StrictPublishFailures)
                throw;
        }
    }

    private Task EmitLoginSucceededAsync(string method, Guid userId, Guid? tenantId, bool requiresTenantSelection, string? traceId, CancellationToken ct)
    {
        var evt = new LoginSucceededEvent
        {
            Method = method,
            AuthUserId = userId.ToString("D"),
            TenantId = tenantId,
            RequiresTenantSelection = requiresTenantSelection,
            TraceId = traceId
        };
        evt.Metadata["idempotencyKey"] = $"{LoginSucceededEvent.TypeName}:{method}:{tenantId?.ToString("D") ?? "pretenant"}:{userId:D}";
        return InvokeLifecycleAndPublishAsync(evt, (hook, e, token) => hook.OnLoginSucceededAsync(e, token), ct);
    }

    private Task EmitLoginFailedAsync(string method, string identifier, string reason, string? correlationId, string? traceId, CancellationToken ct)
    {
        var evt = new LoginFailedEvent
        {
            Method = method,
            Identifier = identifier,
            Reason = reason,
            CorrelationId = correlationId,
            CausationId = correlationId,
            TraceId = traceId
        };
        evt.Metadata["idempotencyKey"] = $"{LoginFailedEvent.TypeName}:{method}:{identifier}:{reason}";
        return InvokeLifecycleAndPublishAsync(evt, (hook, e, token) => hook.OnLoginFailedAsync(e, token), ct);
    }

    private static string? ResolveTraceId()
    {
        var current = Activity.Current;
        if (current is null)
            return null;

        return current.TraceId != default ? current.TraceId.ToString() : current.Id;
    }

    private async Task EnsureNotLockedAsync(string method, string identifier, CancellationToken ct)
    {
        var opts = _attemptOptions.Value;
        if (!opts.Enabled)
            return;

        var state = await _attempts.GetStateAsync(method, identifier, ct);
        if (state.IsLocked(DateTimeOffset.UtcNow))
            throw new IdentityUnauthorizedException("Too many failed login attempts. Try again later.");
    }

    private async Task RegisterFailedAttemptAsync(string method, string identifier, CancellationToken ct)
    {
        var opts = _attemptOptions.Value;
        if (!opts.Enabled)
            return;

        await _attempts.RegisterFailureAsync(
            method,
            identifier,
            opts.MaxFailedAttempts,
            TimeSpan.FromMinutes(opts.LockoutMinutes),
            ct);
    }
}
