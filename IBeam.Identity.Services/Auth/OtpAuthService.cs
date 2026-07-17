using IBeam.Identity.Exceptions;
using IBeam.Identity.Events;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Tenants;
using IBeam.Identity.Services.Users;
using IBeam.Identity.Services.Utils;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace IBeam.Identity.Services.Auth;

public sealed class OtpAuthService : IIdentityOtpAuthService
{
    private const string OtpAttemptMethod = "otp";
    private const string OtpStartAttemptMethod = "otp-start";
    private const string OtpIpAttemptMethod = "otp-ip";
    private const string OtpStartIpAttemptMethod = "otp-start-ip";

    private readonly IIdentityUserStore _users;
    private readonly ITenantMembershipStore _tenants;
    private readonly ITenantProvisioningService _tenantProvisioning;
    private readonly ITokenService _tokens;
    private readonly IOtpService _otpService;
    private readonly IOtpChallengeStore _otpChallengeStore;
    private readonly IAuthAttemptStore _attempts;
    private readonly IAuthAttemptContextProvider _attemptContextProvider;
    private readonly IAuthEventPublisher _eventPublisher;
    private readonly IAuthLifecycleHook _lifecycleHook;
    private readonly IOptions<AuthEventOptions> _eventOptions;
    private readonly IOptions<OtpOptions> _otpOptions;
    private readonly IOptions<TenantProvisioningOptions> _tenantProvisioningOptions;
    private readonly ITenantInfoResolver _tenantInfoResolver;
    private readonly ITenantExtensionCoordinator _tenantExtensions;
    private readonly IIdentityUserExtensionCoordinator _userExtensions;
    private readonly ILogger<OtpAuthService> _logger;
    private readonly IServiceOperationExecutor _operations;

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
        IOptions<OtpOptions> otpOptions,
        IOptions<TenantProvisioningOptions> tenantProvisioningOptions,
        ILogger<OtpAuthService> logger)
        : this(
            users,
            tenants,
            tenantProvisioning,
            tokens,
            otpService,
            otpChallengeStore,
            new Auth.Attempts.InMemoryAuthAttemptStore(),
            new Auth.Attempts.NoOpAuthAttemptContextProvider(),
            eventPublisher,
            lifecycleHook,
            eventOptions,
            otpOptions,
            tenantProvisioningOptions,
            logger)
    {
    }

    public OtpAuthService(
        IIdentityUserStore users,
        ITenantMembershipStore tenants,
        ITenantProvisioningService tenantProvisioning,
        ITokenService tokens,
        IOtpService otpService,
        IOtpChallengeStore otpChallengeStore,
        IAuthAttemptStore attempts,
        IAuthAttemptContextProvider attemptContextProvider,
        IAuthEventPublisher eventPublisher,
        IAuthLifecycleHook lifecycleHook,
        IOptions<AuthEventOptions> eventOptions,
        IOptions<OtpOptions> otpOptions,
        IOptions<TenantProvisioningOptions> tenantProvisioningOptions,
        ILogger<OtpAuthService> logger)
        : this(
            users,
            tenants,
            tenantProvisioning,
            tokens,
            otpService,
            otpChallengeStore,
            attempts,
            attemptContextProvider,
            eventPublisher,
            lifecycleHook,
            eventOptions,
            otpOptions,
            tenantProvisioningOptions,
            new TenantInfoResolver(new NoOpTenantMetadataProvider()),
            new NoOpTenantExtensionCoordinator(),
            new NoOpIdentityUserExtensionCoordinator(),
            logger)
    {
    }

    public OtpAuthService(
        IIdentityUserStore users,
        ITenantMembershipStore tenants,
        ITenantProvisioningService tenantProvisioning,
        ITokenService tokens,
        IOtpService otpService,
        IOtpChallengeStore otpChallengeStore,
        IAuthAttemptStore attempts,
        IAuthAttemptContextProvider attemptContextProvider,
        IAuthEventPublisher eventPublisher,
        IAuthLifecycleHook lifecycleHook,
        IOptions<AuthEventOptions> eventOptions,
        IOptions<OtpOptions> otpOptions,
        IOptions<TenantProvisioningOptions> tenantProvisioningOptions,
        ITenantInfoResolver tenantInfoResolver,
        ITenantExtensionCoordinator tenantExtensions,
        IIdentityUserExtensionCoordinator userExtensions,
        ILogger<OtpAuthService> logger,
        IServiceOperationExecutor? operations = null)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _tenantProvisioning = tenantProvisioning ?? throw new ArgumentNullException(nameof(tenantProvisioning));
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _otpService = otpService ?? throw new ArgumentNullException(nameof(otpService));
        _otpChallengeStore = otpChallengeStore ?? throw new ArgumentNullException(nameof(otpChallengeStore));
        _attempts = attempts ?? throw new ArgumentNullException(nameof(attempts));
        _attemptContextProvider = attemptContextProvider ?? throw new ArgumentNullException(nameof(attemptContextProvider));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _lifecycleHook = lifecycleHook ?? throw new ArgumentNullException(nameof(lifecycleHook));
        _eventOptions = eventOptions ?? throw new ArgumentNullException(nameof(eventOptions));
        _otpOptions = otpOptions ?? throw new ArgumentNullException(nameof(otpOptions));
        _tenantProvisioningOptions = tenantProvisioningOptions ?? throw new ArgumentNullException(nameof(tenantProvisioningOptions));
        _tenantInfoResolver = tenantInfoResolver ?? throw new ArgumentNullException(nameof(tenantInfoResolver));
        _tenantExtensions = tenantExtensions ?? throw new ArgumentNullException(nameof(tenantExtensions));
        _userExtensions = userExtensions ?? throw new ArgumentNullException(nameof(userExtensions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _operations = operations ?? new ServiceOperationExecutor();
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
            new Auth.Attempts.InMemoryAuthAttemptStore(),
            new Auth.Attempts.NoOpAuthAttemptContextProvider(),
            new NoOpAuthEventPublisher(),
            new NoOpAuthLifecycleHook(),
            Microsoft.Extensions.Options.Options.Create(new AuthEventOptions()),
            Microsoft.Extensions.Options.Options.Create(new OtpOptions { AllowAutoProvisionForUnknownUser = true }),
            Microsoft.Extensions.Options.Options.Create(new TenantProvisioningOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OtpAuthService>.Instance)
    {
    }

    [IBeamOperation("identity.auth.otp.start", Permission = false)]
    public async Task<OtpChallengeResult> StartOtpAsync(string destination, Guid? tenantId = null, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => StartOtpCoreAsync(destination, tenantId, token),
            new ServiceOperationExecutionOptions
            {
                TenantId = tenantId,
                PermissionEnabled = false
            },
            ct).ConfigureAwait(false);

    private async Task<OtpChallengeResult> StartOtpCoreAsync(string destination, Guid? tenantId, CancellationToken ct)
    {
        IdentityUtils.ThrowIfNullOrWhiteSpace(destination, nameof(destination));

        var (channel, normalized) = IdentityUtils.NormalizeDestination(destination);
        var attemptContext = GetAttemptContext();
        await EnsureOtpAttemptAllowedAsync(OtpAttemptMethod, normalized, ct).ConfigureAwait(false);
        await EnsureOtpAttemptAllowedAsync(OtpStartAttemptMethod, normalized, ct).ConfigureAwait(false);
        await EnsureOtpIpAllowedAsync(OtpIpAttemptMethod, attemptContext, ct).ConfigureAwait(false);
        await EnsureOtpIpAllowedAsync(OtpStartIpAttemptMethod, attemptContext, ct).ConfigureAwait(false);
        await RegisterOtpStartAttemptAsync(normalized, attemptContext, ct).ConfigureAwait(false);

        var existingUser = channel == SenderChannel.Email
            ? await _users.FindByEmailAsync(normalized, ct)
            : await _users.FindByPhoneAsync(normalized, ct);
        if (existingUser is null && !_otpOptions.Value.AllowAutoProvisionForUnknownUser)
        {
            _logger.LogWarning(
                "auth.startotp.blocked_unknown_user destination={Destination} channel={Channel}",
                normalized,
                channel);
            throw new IdentityUnauthorizedException("Unauthorized.");
        }

        var effectiveTenantId = ResolveEffectiveTenantId(tenantId);
        var traceId = ResolveTraceId();
        var requested = new OtpChallengeRequestedEvent
        {
            Destination = normalized,
            Purpose = SenderPurpose.LoginMfa.ToString(),
            TenantId = effectiveTenantId,
            TraceId = traceId
        };
        requested.Metadata["idempotencyKey"] =
            $"{OtpChallengeRequestedEvent.TypeName}:{SenderPurpose.LoginMfa}:{normalized}";
        await InvokeLifecycleAndPublishAsync(
            requested,
            (hook, evt, token) => hook.OnBeforeOtpChallengeCreateAsync(evt, token),
            ct);

        var request = new OtpChallengeRequest(channel, normalized, SenderPurpose.LoginMfa, effectiveTenantId);
        var challenge = await _otpService.CreateChallengeAsync(request, ct);

        var challengeCreated = new OtpChallengeCreatedEvent
        {
            ChallengeId = challenge.ChallengeId,
            Destination = normalized,
            Purpose = SenderPurpose.LoginMfa.ToString(),
            CorrelationId = challenge.ChallengeId,
            CausationId = challenge.ChallengeId,
            TraceId = traceId
        };
        challengeCreated.Metadata["idempotencyKey"] = $"{OtpChallengeCreatedEvent.TypeName}:{challenge.ChallengeId}";
        await InvokeLifecycleAndPublishAsync(
            challengeCreated,
            (hook, evt, token) => hook.OnOtpChallengeCreatedAsync(evt, token),
            ct);

        return challenge;
    }

    [IBeamOperation("identity.auth.otp.complete", Permission = false)]
    public async Task<AuthResultResponse> CompleteOtpAsync(
        string challengeId,
        string code,
        string destination,
        string? displayName = null,
        CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            token => CompleteOtpCoreAsync(challengeId, code, destination, displayName, token),
            new ServiceOperationExecutionOptions { PermissionEnabled = false },
            ct).ConfigureAwait(false);

    private async Task<AuthResultResponse> CompleteOtpCoreAsync(
        string challengeId,
        string code,
        string destination,
        string? displayName,
        CancellationToken ct)
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
        var traceId = ResolveTraceId();
        var attemptContext = GetAttemptContext(traceId);

        await EnsureOtpAttemptAllowedAsync(OtpAttemptMethod, normalizedDestination, ct).ConfigureAwait(false);
        await EnsureOtpIpAllowedAsync(OtpIpAttemptMethod, attemptContext, ct).ConfigureAwait(false);

        var loginAttempt = new LoginAttemptedEvent
        {
            Method = "otp",
            Identifier = normalizedDestination,
            CorrelationId = challengeId,
            CausationId = challengeId,
            TraceId = traceId
        };
        loginAttempt.Metadata["idempotencyKey"] = $"{LoginAttemptedEvent.TypeName}:otp:{challengeId}";
        await InvokeLifecycleAndPublishAsync(
            loginAttempt,
            (hook, evt, token) => hook.OnBeforeLoginAsync(evt, token),
            ct);

        var verifyRequested = new OtpVerifyRequestedEvent
        {
            ChallengeId = challengeId,
            Destination = normalizedDestination,
            CorrelationId = challengeId,
            CausationId = challengeId,
            TraceId = traceId
        };
        verifyRequested.Metadata["idempotencyKey"] = $"{OtpVerifyRequestedEvent.TypeName}:{challengeId}";
        await InvokeLifecycleAndPublishAsync(
            verifyRequested,
            (hook, evt, token) => hook.OnBeforeOtpVerifyAsync(evt, token),
            ct);

        var verifyResult = await _otpService.VerifyAsync(new OtpVerifyRequest(challengeId, code), ct);
        if (!verifyResult.Success)
        {
            var resumed = await TryResumeConsumedChallengeAsync(challengeId, ct).ConfigureAwait(false);
            if (resumed is null)
            {
                await RegisterOtpVerificationFailureAsync(normalizedDestination, attemptContext, ct)
                    .ConfigureAwait(false);
                verifyResult = await FailOtpVerificationAsync(challengeId, normalizedDestination, traceId, ct)
                    .ConfigureAwait(false);
            }
            else
            {
                verifyResult = resumed;
            }
        }

        var verified = new OtpVerifiedEvent
        {
            ChallengeId = challengeId,
            CorrelationId = challengeId,
            CausationId = challengeId,
            TraceId = traceId
        };
        verified.Metadata["idempotencyKey"] = $"{OtpVerifiedEvent.TypeName}:{challengeId}";
        await InvokeLifecycleAndPublishAsync(
            verified,
            (hook, evt, token) => hook.OnOtpVerifiedAsync(evt, token),
            ct);

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

        IdentityUser? user = await FindUserByDestinationAsync(channel, normalizedDestination, ct)
            .ConfigureAwait(false);

        var createdNewUser = false;
        if (user is null)
        {
            if (!_otpOptions.Value.AllowAutoProvisionForUnknownUser)
            {
                _logger.LogWarning(
                    "auth.completeotp.blocked_auto_provision challengeId={ChallengeId} destination={Destination} channel={Channel}",
                    challengeId,
                    normalizedDestination,
                    channel);
                throw new IdentityUnauthorizedException("Unauthorized.");
            }

            var createRequest = channel == SenderChannel.Email
                ? new RegisterUserRequest(normalizedDestination, null, string.Empty, displayName)
                : new RegisterUserRequest(null, normalizedDestination, string.Empty, displayName);

            var userCreateRequested = new AuthUserCreateRequestedEvent
            {
                NormalizedEmail = channel == SenderChannel.Email ? normalizedDestination.Trim().ToLowerInvariant() : null,
                NormalizedPhone = channel == SenderChannel.Sms ? normalizedDestination.Trim() : null,
                CorrelationId = challengeId,
                CausationId = challengeId,
                TraceId = traceId
            };
            userCreateRequested.Metadata["idempotencyKey"] =
                $"{AuthUserCreateRequestedEvent.TypeName}:{(userCreateRequested.NormalizedEmail ?? userCreateRequested.NormalizedPhone ?? challengeId)}";
            await InvokeLifecycleAndPublishAsync(
                userCreateRequested,
                (hook, evt, token) => hook.OnBeforeAuthUserCreateAsync(evt, token),
                ct);

            CreateUserResult createResult;
            try
            {
                createResult = await _users.CreateAsync(createRequest, ct).ConfigureAwait(false);
            }
            catch (IdentityException)
            {
                var existing = await FindUserByDestinationAsync(channel, normalizedDestination, ct)
                    .ConfigureAwait(false);
                if (existing is null)
                    throw;

                user = existing;
                createdNewUser = false;
                createResult = CreateUserResult.Success(existing);
            }

            if (!createResult.Succeeded || createResult.User is null)
            {
                var existing = await FindUserByDestinationAsync(channel, normalizedDestination, ct)
                    .ConfigureAwait(false);
                if (existing is not null)
                {
                    user = existing;
                    createdNewUser = false;
                    createResult = CreateUserResult.Success(existing);
                }
            }

            if (!createResult.Succeeded || createResult.User is null)
                throw new IdentityValidationException("User creation failed.", createResult.Errors);

            if (user is null)
            {
                user = createResult.User with { DisplayName = displayName ?? createResult.User.DisplayName };
                createdNewUser = true;
            }

            if (createdNewUser)
            {
                var userCreatedEvent = new AuthUserCreatedEvent
                {
                    AuthUserId = user.UserId.ToString("D"),
                    NormalizedEmail = string.IsNullOrWhiteSpace(user.Email) ? null : user.Email.Trim().ToLowerInvariant(),
                    NormalizedPhone = string.IsNullOrWhiteSpace(user.PhoneNumber) ? null : user.PhoneNumber.Trim(),
                    CorrelationId = challengeId,
                    CausationId = challengeId,
                    TraceId = traceId
                };
                userCreatedEvent.Metadata["idempotencyKey"] = $"{AuthUserCreatedEvent.TypeName}:{user.UserId:D}";

                await InvokeLifecycleAndPublishAsync(
                    userCreatedEvent,
                    (hook, evt, token) => hook.OnAuthUserCreatedAsync(evt, token),
                    ct);
                await OnUserCreatedExtensionAsync(user, null, challengeId, challengeId, traceId, userCreatedEvent.Metadata, ct)
                    .ConfigureAwait(false);
            }
        }

        var activeTenants = await GetActiveTenantsForUserAsync(user.UserId, ct);

        var policyTenantId = ResolveEffectiveTenantId(challenge.TenantId);
        activeTenants = await ApplyTenantProvisioningPolicyAsync(
            user,
            createdNewUser,
            activeTenants,
            policyTenantId,
            challengeId,
            traceId,
            channel == SenderChannel.Email ? normalizedDestination : user.Email,
            ct);

        await EnsureTenantExtensionsForUserAsync(
            user.UserId,
            TenantExtensionOperations.Ensure,
            challengeId,
            challengeId,
            traceId,
            ct);

        if (policyTenantId.HasValue)
        {
            var requestedTenant = activeTenants.FirstOrDefault(t => t.TenantId == policyTenantId.Value);
            if (requestedTenant is null)
                throw new IdentityValidationException($"User is not linked to tenant '{policyTenantId.Value:D}'.");

            activeTenants = new List<TenantInfo> { requestedTenant };
        }

        if (activeTenants.Count == 1)
        {
            var tenant = activeTenants[0];
            var claims = BuildBaseClaims(user);
            AddTenantClaims(claims, tenant.TenantId);
            AddRoleClaims(claims, tenant.Roles);
            AddRoleIdClaims(claims, tenant.RoleIds);
            await EnsureUserExtensionAsync(user, tenant.TenantId, UserExtensionOperations.Login, challengeId, challengeId, traceId, ct)
                .ConfigureAwait(false);
            var token = await _tokens.CreateAccessTokenAsync(user.UserId, tenant.TenantId, claims, ct);
            await RegisterOtpSuccessAsync(normalizedDestination, attemptContext, ct).ConfigureAwait(false);
            await EmitLoginSucceededAsync("otp", user.UserId, tenant.TenantId, false, challengeId, traceId, ct);
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
                AddRoleIdClaims(claims, defaultTenant.RoleIds);
                await EnsureUserExtensionAsync(user, defaultTenant.TenantId, UserExtensionOperations.Login, challengeId, challengeId, traceId, ct)
                    .ConfigureAwait(false);
                var token = await _tokens.CreateAccessTokenAsync(user.UserId, defaultTenant.TenantId, claims, ct);
                await RegisterOtpSuccessAsync(normalizedDestination, attemptContext, ct).ConfigureAwait(false);
                await EmitLoginSucceededAsync("otp", user.UserId, defaultTenant.TenantId, false, challengeId, traceId, ct);
                return AuthResultResponse.WithToken(token, createdNewUser);
            }
        }

        var preClaims = BuildBaseClaims(user);
        preClaims.Add(new ClaimItem("pt", "1"));
        var preToken = await _tokens.CreatePreTenantTokenAsync(user.UserId, preClaims, ct);
        await RegisterOtpSuccessAsync(normalizedDestination, attemptContext, ct).ConfigureAwait(false);
        await EmitLoginSucceededAsync("otp", user.UserId, null, true, challengeId, traceId, ct);
        return AuthResultResponse.RequiresSelection(preToken.AccessToken, activeTenants, createdNewUser);
    }

    private async Task<List<TenantInfo>> ApplyTenantProvisioningPolicyAsync(
        IdentityUser user,
        bool createdNewUser,
        List<TenantInfo> activeTenants,
        Guid? requestedTenantId,
        string correlationId,
        string? traceId,
        string? email,
        CancellationToken ct)
    {
        var options = _tenantProvisioningOptions.Value;

        if (options.Mode == TenantProvisioningMode.RequireExistingTenant)
        {
            if (requestedTenantId.HasValue)
            {
                if (activeTenants.Any(t => t.TenantId == requestedTenantId.Value))
                    return activeTenants;

                throw new IdentityValidationException($"User is not linked to required tenant '{requestedTenantId.Value:D}'.");
            }

            if (activeTenants.Count == 0)
                throw new IdentityValidationException("User does not have an active tenant membership.");

            return activeTenants;
        }

        if (options.Mode == TenantProvisioningMode.UseDefaultTenant)
        {
            var defaultTenantId = requestedTenantId ?? options.DefaultTenantId;
            if (!defaultTenantId.HasValue || defaultTenantId.Value == Guid.Empty)
                throw new IdentityValidationException("Default tenant id is required.");

            if (activeTenants.Any(t => t.TenantId == defaultTenantId.Value))
                return activeTenants;

            if (!options.AutoLinkUserToDefaultTenant)
                throw new IdentityValidationException($"User is not linked to default tenant '{defaultTenantId.Value:D}'.");

            await _tenantProvisioning.EnsureUserTenantMembershipAsync(
                defaultTenantId.Value,
                user.UserId,
                roleNames: options.AutoLinkRoleNames,
                setAsDefault: true,
                ct: ct);

            activeTenants = await GetActiveTenantsForUserAsync(user.UserId, ct);

            if (!activeTenants.Any(t => t.TenantId == defaultTenantId.Value))
                throw new IdentityProviderException("Default tenant membership could not be resolved after linking.");

            return activeTenants;
        }

        if (createdNewUser || activeTenants.Count == 0)
        {
            var tenantCreateRequested = new TenantCreateRequestedEvent
            {
                AuthUserId = user.UserId.ToString("D"),
                SuggestedTenantName = string.IsNullOrWhiteSpace(email) ? null : $"{email.Split('@')[0]}'s Workspace",
                CorrelationId = correlationId,
                CausationId = correlationId,
                TraceId = traceId
            };
            tenantCreateRequested.Metadata["idempotencyKey"] =
                $"{TenantCreateRequestedEvent.TypeName}:{user.UserId:D}";
            await InvokeLifecycleAndPublishAsync(
                tenantCreateRequested,
                (hook, evt, token) => hook.OnBeforeTenantCreateAsync(evt, token),
                ct);

            var createdTenantId = await _tenantProvisioning.CreateTenantForNewUserAsync(user.UserId, email, ct);
            activeTenants = await GetActiveTenantsForUserAsync(user.UserId, ct);

            if (!activeTenants.Any(t => t.TenantId == createdTenantId))
                throw new IdentityProviderException("Tenant provisioning completed but membership could not be resolved.");

            var createdTenant = activeTenants.FirstOrDefault(t => t.TenantId == createdTenantId);
            var tenantCreatedEvent = new TenantCreatedEvent
            {
                TenantId = createdTenantId,
                TenantName = createdTenant?.Name,
                Status = IdentityTenantStatuses.Active,
                CorrelationId = correlationId,
                CausationId = correlationId,
                TraceId = traceId
            };
            tenantCreatedEvent.Metadata["idempotencyKey"] = $"{TenantCreatedEvent.TypeName}:{createdTenantId:D}";

            await InvokeLifecycleAndPublishAsync(
                tenantCreatedEvent,
                (hook, evt, token) => hook.OnTenantCreatedAsync(evt, token),
                ct);

            if (createdTenant is not null)
            {
                await _tenantExtensions.OnTenantCreatedAsync(
                    IdentityTenant.FromTenantInfo(createdTenant, DateTimeOffset.UtcNow),
                    TenantExtensionContext.Create(
                        TenantExtensionOperations.Created,
                        user.UserId,
                        correlationId,
                        correlationId,
                        traceId,
                        tenantCreatedEvent.Metadata),
                    ct).ConfigureAwait(false);
            }

            var linkedRole = createdTenant?.Roles?.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r));
            var linkRequested = new TenantUserLinkRequestedEvent
            {
                TenantId = createdTenantId,
                AuthUserId = user.UserId.ToString("D"),
                CorrelationId = correlationId,
                CausationId = correlationId,
                TraceId = traceId
            };
            linkRequested.Metadata["idempotencyKey"] = $"{TenantUserLinkRequestedEvent.TypeName}:{createdTenantId:D}:{user.UserId:D}";
            await InvokeLifecycleAndPublishAsync(
                linkRequested,
                (hook, evt, token) => hook.OnBeforeTenantUserLinkAsync(evt, token),
                ct);

            var tenantUserLinkedEvent = new TenantUserLinkedEvent
            {
                TenantId = createdTenantId,
                AuthUserId = user.UserId.ToString("D"),
                Role = linkedRole,
                UserTenantId = $"{createdTenantId:D}|{user.UserId:D}",
                CorrelationId = correlationId,
                CausationId = correlationId,
                TraceId = traceId
            };
            tenantUserLinkedEvent.Metadata["idempotencyKey"] =
                $"{TenantUserLinkedEvent.TypeName}:{createdTenantId:D}:{user.UserId:D}";

            await InvokeLifecycleAndPublishAsync(
                tenantUserLinkedEvent,
                (hook, evt, token) => hook.OnTenantUserLinkedAsync(evt, token),
                ct);
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

    private async Task<OtpVerifyResult?> TryResumeConsumedChallengeAsync(string challengeId, CancellationToken ct)
    {
        var challenge = await _otpChallengeStore.GetAsync(challengeId, ct).ConfigureAwait(false);
        if (challenge is null || !challenge.IsConsumed)
            return null;

        if (string.IsNullOrWhiteSpace(challenge.VerificationToken) ||
            !challenge.VerificationTokenExpiresAt.HasValue ||
            challenge.VerificationTokenExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            return null;
        }

        return new OtpVerifyResult(
            true,
            challenge.VerificationToken,
            challenge.VerificationTokenExpiresAt);
    }

    private async Task<OtpVerifyResult> FailOtpVerificationAsync(
        string challengeId,
        string normalizedDestination,
        string? traceId,
        CancellationToken ct)
    {
        var failed = new OtpVerificationFailedEvent
        {
            ChallengeId = challengeId,
            Reason = "OTP verification failed.",
            CorrelationId = challengeId,
            CausationId = challengeId,
            TraceId = traceId
        };
        failed.Metadata["idempotencyKey"] = $"{OtpVerificationFailedEvent.TypeName}:{challengeId}";
        await InvokeLifecycleAndPublishAsync(
            failed,
            (hook, evt, token) => hook.OnOtpVerificationFailedAsync(evt, token),
            ct);

        var loginFailed = new LoginFailedEvent
        {
            Method = "otp",
            Identifier = normalizedDestination,
            Reason = "OTP verification failed.",
            CorrelationId = challengeId,
            CausationId = challengeId,
            TraceId = traceId
        };
        loginFailed.Metadata["idempotencyKey"] = $"{LoginFailedEvent.TypeName}:otp:{challengeId}";
        await InvokeLifecycleAndPublishAsync(
            loginFailed,
            (hook, evt, token) => hook.OnLoginFailedAsync(evt, token),
            ct);

        await DelayFailedOtpResponseAsync(ct).ConfigureAwait(false);
        throw new IdentityValidationException("OTP verification failed.");
    }

    private AuthAttemptContext GetAttemptContext(string? correlationId = null)
    {
        var context = _attemptContextProvider.GetCurrent();
        if (!string.IsNullOrWhiteSpace(context.CorrelationId) || string.IsNullOrWhiteSpace(correlationId))
            return context;

        return context with { CorrelationId = correlationId };
    }

    private async Task EnsureOtpAttemptAllowedAsync(string method, string identifier, CancellationToken ct)
    {
        var options = _otpOptions.Value;
        var enabled = method == OtpStartAttemptMethod
            ? options.MaxChallengeRequests > 0
            : options.MaxAttempts > 0;
        if (!enabled)
            return;

        var state = await _attempts.GetStateAsync(method, identifier, ct).ConfigureAwait(false);
        if (state.IsLocked(DateTimeOffset.UtcNow))
        {
            await DelayFailedOtpResponseAsync(ct).ConfigureAwait(false);
            throw new IdentityUnauthorizedException("OTP verification failed.");
        }
    }

    private async Task EnsureOtpIpAllowedAsync(string method, AuthAttemptContext context, CancellationToken ct)
    {
        var options = _otpOptions.Value;
        var enabled = method == OtpStartIpAttemptMethod
            ? options.MaxChallengeRequests > 0
            : options.MaxFailedAttemptsPerIp > 0;
        if (!enabled || string.IsNullOrWhiteSpace(context.IpAddress))
            return;

        var state = await _attempts.GetStateAsync(method, context.IpAddress!, ct).ConfigureAwait(false);
        if (state.IsLocked(DateTimeOffset.UtcNow))
        {
            await DelayFailedOtpResponseAsync(ct).ConfigureAwait(false);
            throw new IdentityUnauthorizedException("OTP verification failed.");
        }
    }

    private async Task RegisterOtpStartAttemptAsync(string normalizedDestination, AuthAttemptContext context, CancellationToken ct)
    {
        var options = _otpOptions.Value;
        if (options.MaxChallengeRequests <= 0)
            return;

        await _attempts.RegisterFailureAsync(
            OtpStartAttemptMethod,
            normalizedDestination,
            options.MaxChallengeRequests,
            TimeSpan.FromMinutes(options.ChallengeRequestLockoutMinutes),
            ct,
            options.TrackAttemptMetadata ? context : null).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(context.IpAddress))
        {
            await _attempts.RegisterFailureAsync(
                OtpStartIpAttemptMethod,
                context.IpAddress!,
                options.MaxChallengeRequests,
                TimeSpan.FromMinutes(options.ChallengeRequestLockoutMinutes),
                ct,
                options.TrackAttemptMetadata ? context : null).ConfigureAwait(false);
        }
    }

    private async Task RegisterOtpVerificationFailureAsync(string normalizedDestination, AuthAttemptContext context, CancellationToken ct)
    {
        var options = _otpOptions.Value;
        if (options.MaxAttempts > 0)
        {
            await _attempts.RegisterFailureAsync(
                OtpAttemptMethod,
                normalizedDestination,
                options.MaxAttempts,
                TimeSpan.FromMinutes(options.LockoutMinutes),
                ct,
                options.TrackAttemptMetadata ? context : null).ConfigureAwait(false);
        }

        if (options.MaxFailedAttemptsPerIp > 0 && !string.IsNullOrWhiteSpace(context.IpAddress))
        {
            await _attempts.RegisterFailureAsync(
                OtpIpAttemptMethod,
                context.IpAddress!,
                options.MaxFailedAttemptsPerIp,
                TimeSpan.FromMinutes(options.IpLockoutMinutes),
                ct,
                options.TrackAttemptMetadata ? context : null).ConfigureAwait(false);
        }
    }

    private async Task RegisterOtpSuccessAsync(string normalizedDestination, AuthAttemptContext context, CancellationToken ct)
    {
        var storeContext = _otpOptions.Value.TrackAttemptMetadata ? context : null;
        await _attempts.RegisterSuccessAsync(OtpAttemptMethod, normalizedDestination, ct, storeContext)
            .ConfigureAwait(false);
        await _attempts.RegisterSuccessAsync(OtpStartAttemptMethod, normalizedDestination, ct, storeContext)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(context.IpAddress))
        {
            await _attempts.RegisterSuccessAsync(OtpIpAttemptMethod, context.IpAddress!, ct, storeContext)
                .ConfigureAwait(false);
            await _attempts.RegisterSuccessAsync(OtpStartIpAttemptMethod, context.IpAddress!, ct, storeContext)
                .ConfigureAwait(false);
        }
    }

    private Task DelayFailedOtpResponseAsync(CancellationToken ct)
    {
        var delay = _otpOptions.Value.FailureResponseDelayMilliseconds;
        return delay > 0 ? Task.Delay(delay, ct) : Task.CompletedTask;
    }

    private Task<IdentityUser?> FindUserByDestinationAsync(
        SenderChannel channel,
        string normalizedDestination,
        CancellationToken ct)
        => channel == SenderChannel.Email
            ? _users.FindByEmailAsync(normalizedDestination, ct)
            : _users.FindByPhoneAsync(normalizedDestination, ct);

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
            await EnsureTenantExtensionAsync(tenant, userId, operation, correlationId, causationId, traceId, ct)
                .ConfigureAwait(false);
        }
    }

    private Task EnsureTenantExtensionAsync(
        TenantInfo tenant,
        Guid userId,
        string operation,
        string? correlationId,
        string? causationId,
        string? traceId,
        CancellationToken ct)
        => _tenantExtensions.EnsureExtensionAsync(
            IdentityTenant.FromTenantInfo(tenant),
            TenantExtensionContext.Create(operation, userId, correlationId, causationId, traceId),
            ct);

    private Task OnUserCreatedExtensionAsync(
        IdentityUser user,
        Guid? tenantId,
        string? correlationId,
        string? causationId,
        string? traceId,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken ct)
        => _userExtensions.OnUserCreatedAsync(
            user,
            CreateUserExtensionContext(user, UserExtensionOperations.Created, tenantId, correlationId, causationId, traceId, metadata),
            ct);

    private Task EnsureUserExtensionAsync(
        IdentityUser user,
        Guid? tenantId,
        string operation,
        string? correlationId,
        string? causationId,
        string? traceId,
        CancellationToken ct)
        => _userExtensions.EnsureExtensionAsync(
            user,
            CreateUserExtensionContext(user, operation, tenantId, correlationId, causationId, traceId, null),
            ct);

    private static UserExtensionContext CreateUserExtensionContext(
        IdentityUser user,
        string operation,
        Guid? tenantId,
        string? correlationId,
        string? causationId,
        string? traceId,
        IReadOnlyDictionary<string, string>? metadata)
        => UserExtensionContext.Create(
            operation,
            user.UserId,
            tenantId,
            user.Email,
            user.PhoneNumber,
            user.DisplayName,
            correlationId: correlationId,
            causationId: causationId,
            traceId: traceId,
            metadata: metadata);

    private Guid? ResolveEffectiveTenantId(Guid? requestedTenantId)
    {
        if (requestedTenantId.HasValue && requestedTenantId.Value != Guid.Empty)
            return requestedTenantId;

        var options = _tenantProvisioningOptions.Value;
        return options.Mode == TenantProvisioningMode.UseDefaultTenant
            ? options.DefaultTenantId
            : null;
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

    private static void AddRoleIdClaims(List<ClaimItem> claims, IEnumerable<Guid>? roleIds)
    {
        if (roleIds is null) return;

        foreach (var roleId in roleIds.Where(x => x != Guid.Empty).Distinct())
            claims.Add(new ClaimItem("rid", roleId.ToString("D")));
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

    private Task EmitLoginSucceededAsync(
        string method,
        Guid userId,
        Guid? tenantId,
        bool requiresTenantSelection,
        string correlationId,
        string? traceId,
        CancellationToken ct)
    {
        var evt = new LoginSucceededEvent
        {
            Method = method,
            AuthUserId = userId.ToString("D"),
            TenantId = tenantId,
            RequiresTenantSelection = requiresTenantSelection,
            CorrelationId = correlationId,
            CausationId = correlationId,
            TraceId = traceId
        };
        evt.Metadata["idempotencyKey"] =
            $"{LoginSucceededEvent.TypeName}:{method}:{tenantId?.ToString("D") ?? "pretenant"}:{userId:D}";
        return InvokeLifecycleAndPublishAsync(evt, (hook, e, token) => hook.OnLoginSucceededAsync(e, token), ct);
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
