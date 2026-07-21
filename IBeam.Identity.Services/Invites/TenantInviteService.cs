using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using IBeam.AccessControl;
using IBeam.Identity.Events;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Utils;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IBeam.Identity.Services.Invites;

[IBeamOperation("identity.tenantinvites")]
public sealed class TenantInviteService : ITenantInviteService
{
    private static readonly TimeSpan DefaultInviteLifetime = TimeSpan.FromDays(7);

    private readonly ITenantInviteStore _invites;
    private readonly IIdentityTenantService _tenants;
    private readonly IIdentityUserStore _users;
    private readonly ITenantRoleService _roles;
    private readonly ITenantProvisioningService _tenantProvisioning;
    private readonly ITenantMembershipStore _memberships;
    private readonly IIdentityUserExtensionCoordinator _userExtensions;
    private readonly IIdentityCommunicationSender _sender;
    private readonly ITenantInviteUrlBuilder _urlBuilder;
    private readonly ITenantInviteMessageFactory _messageFactory;
    private readonly IOtpService _otpService;
    private readonly IOtpChallengeStore _otpChallenges;
    private readonly ITokenService _tokens;
    private readonly IAuthEventPublisher _events;
    private readonly IOptions<AuthEventOptions> _eventOptions;
    private readonly ILogger<TenantInviteService> _logger;
    private readonly IServiceOperationExecutor _operations;
    private readonly IResourceAccessService? _resourceAccess;

    public TenantInviteService(
        ITenantInviteStore invites,
        IIdentityTenantService tenants,
        IIdentityUserStore users,
        ITenantRoleService roles,
        ITenantProvisioningService tenantProvisioning,
        ITenantMembershipStore memberships,
        IIdentityUserExtensionCoordinator userExtensions,
        IIdentityCommunicationSender sender,
        ITenantInviteUrlBuilder urlBuilder,
        ITenantInviteMessageFactory messageFactory,
        IOtpService otpService,
        IOtpChallengeStore otpChallenges,
        ITokenService tokens,
        IAuthEventPublisher events,
        IOptions<AuthEventOptions> eventOptions,
        ILogger<TenantInviteService> logger,
        IResourceAccessService? resourceAccess = null,
        IServiceOperationExecutor? operations = null)
    {
        _invites = invites ?? throw new ArgumentNullException(nameof(invites));
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _roles = roles ?? throw new ArgumentNullException(nameof(roles));
        _tenantProvisioning = tenantProvisioning ?? throw new ArgumentNullException(nameof(tenantProvisioning));
        _memberships = memberships ?? throw new ArgumentNullException(nameof(memberships));
        _userExtensions = userExtensions ?? throw new ArgumentNullException(nameof(userExtensions));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _urlBuilder = urlBuilder ?? throw new ArgumentNullException(nameof(urlBuilder));
        _messageFactory = messageFactory ?? throw new ArgumentNullException(nameof(messageFactory));
        _otpService = otpService ?? throw new ArgumentNullException(nameof(otpService));
        _otpChallenges = otpChallenges ?? throw new ArgumentNullException(nameof(otpChallenges));
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _eventOptions = eventOptions ?? throw new ArgumentNullException(nameof(eventOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resourceAccess = resourceAccess;
        _operations = operations ?? new ServiceOperationExecutor();
    }

    [IBeamOperation("identity.tenantinvites.create")]
    public Task<TenantInviteCreatedResult> CreateInviteAsync(Guid tenantId, TenantInviteCreateRequest request, Guid invitedByUserId, CancellationToken ct = default)
        => _operations.ExecuteAsync(
            this,
            token => CreateInviteCoreAsync(tenantId, request, invitedByUserId, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = invitedByUserId },
            ct);

    [IBeamOperation("identity.tenantinvites.list")]
    public async Task<IReadOnlyList<TenantInviteInfo>> ListInvitesAsync(Guid tenantId, CancellationToken ct = default)
        => await ListInvitesAsync(tenantId, null, ct).ConfigureAwait(false);

    [IBeamOperation("identity.tenantinvites.list")]
    public async Task<IReadOnlyList<TenantInviteInfo>> ListInvitesAsync(Guid tenantId, TenantInviteListRequest? request, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            async token =>
            {
                var invites = (await _invites.ListForTenantAsync(tenantId, token).ConfigureAwait(false))
                    .Select(TenantInviteInfo.FromRecord)
                    .ToList();

                return ApplyListFilter(invites, request, DateTimeOffset.UtcNow);
            },
            new ServiceOperationExecutionOptions { TenantId = tenantId },
            ct).ConfigureAwait(false);

    [IBeamOperation("identity.tenantinvites.get")]
    public async Task<TenantInviteInfo?> GetInviteAsync(Guid tenantId, Guid inviteId, CancellationToken ct = default)
        => await _operations.ExecuteAsync(
            this,
            async token =>
            {
                var invite = await _invites.GetAsync(tenantId, inviteId, token).ConfigureAwait(false);
                return invite is null ? null : TenantInviteInfo.FromRecord(invite);
            },
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = inviteId },
            ct).ConfigureAwait(false);

    [IBeamOperation("identity.tenantinvites.resend")]
    public Task<TenantInviteCreatedResult> ResendInviteAsync(Guid tenantId, Guid inviteId, Guid resentByUserId, CancellationToken ct = default)
        => _operations.ExecuteAsync(
            this,
            token => ResendInviteCoreAsync(tenantId, inviteId, resentByUserId, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = inviteId },
            ct);

    [IBeamOperation("identity.tenantinvites.revoke")]
    public Task<TenantInviteInfo> RevokeInviteAsync(Guid tenantId, Guid inviteId, Guid revokedByUserId, string? reason = null, CancellationToken ct = default)
        => _operations.ExecuteAsync(
            this,
            token => RevokeInviteCoreAsync(tenantId, inviteId, revokedByUserId, reason, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = inviteId },
            ct);

    [IBeamOperation("identity.tenantinvites.preview", Permission = false)]
    public Task<TenantInvitePreview> PreviewInviteAsync(string tokenOrCode, CancellationToken ct = default)
        => _operations.ExecuteAsync(
            this,
            token => PreviewInviteCoreAsync(tokenOrCode, token),
            new ServiceOperationExecutionOptions { PermissionEnabled = false },
            ct);

    [IBeamOperation("identity.tenantinvites.accept", Permission = false)]
    public Task<TenantInviteAcceptResult> AcceptInviteAsync(TenantInviteAcceptRequest request, Guid? authenticatedUserId = null, CancellationToken ct = default)
        => _operations.ExecuteAsync(
            this,
            token => AcceptInviteCoreAsync(request, authenticatedUserId, token),
            new ServiceOperationExecutionOptions { PermissionEnabled = false },
            ct);

    private async Task<TenantInviteCreatedResult> CreateInviteCoreAsync(
        Guid tenantId,
        TenantInviteCreateRequest request,
        Guid invitedByUserId,
        CancellationToken ct)
    {
        ValidateTenantAndUser(tenantId, invitedByUserId);
        if (request is null) throw new ArgumentNullException(nameof(request));

        var tenant = await _tenants.FindByIdAsync(tenantId, ct).ConfigureAwait(false)
            ?? throw new IdentityValidationException("Tenant not found.");
        var (destinationType, normalizedDestination) = NormalizeDestination(request);
        var token = CreateInviteToken();
        var now = DateTimeOffset.UtcNow;
        var expiresUtc = request.ExpiresUtc?.ToUniversalTime() ?? now.Add(DefaultInviteLifetime);
        if (expiresUtc <= now)
            throw new IdentityValidationException("Invite expiration must be in the future.");

        var inviteId = Guid.NewGuid();
        var correlationId = NormalizeOptional(request.CorrelationId) ?? inviteId.ToString("D");
        var causationId = NormalizeOptional(request.CausationId) ?? correlationId;
        var invite = new TenantInviteRecord(
            inviteId,
            tenantId,
            destinationType,
            normalizedDestination,
            HashToken(token),
            TenantInviteStatuses.Pending,
            now,
            invitedByUserId,
            expiresUtc,
            ProfileHints: new TenantInviteProfileHints(
                NormalizeOptional(request.DisplayName),
                NormalizeOptional(request.FirstName),
                NormalizeOptional(request.LastName),
                NormalizeMetadata(request.Metadata)),
            RoleIds: NormalizeRoleIds(request.RoleIds),
            RoleNames: NormalizeRoleNames(request.RoleNames),
            SetAsDefaultTenant: request.SetAsDefaultTenant,
            AccessGrants: NormalizeAccessGrants(request.AccessGrants),
            RedirectUrl: NormalizeOptional(request.RedirectUrl),
            CorrelationId: correlationId,
            CausationId: causationId,
            Metadata: NormalizeMetadata(request.Metadata),
            RequirePasswordSetup: request.RequirePasswordSetup);

        await PublishInviteEventAsync(new TenantInviteCreateRequestedEvent
        {
            InviteId = invite.InviteId,
            TenantId = invite.TenantId,
            DestinationType = invite.DestinationType,
            NormalizedDestination = invite.NormalizedDestination,
            InvitedByUserId = invitedByUserId,
            CorrelationId = correlationId,
            CausationId = causationId,
            TraceId = ResolveTraceId()
        }, ct).ConfigureAwait(false);

        invite = await _invites.CreateAsync(invite, ct).ConfigureAwait(false);

        await PublishInviteEventAsync(new TenantInviteCreatedEvent
        {
            InviteId = invite.InviteId,
            TenantId = invite.TenantId,
            DestinationType = invite.DestinationType,
            NormalizedDestination = invite.NormalizedDestination,
            InvitedByUserId = invitedByUserId,
            ExpiresUtc = invite.ExpiresUtc,
            CorrelationId = correlationId,
            CausationId = causationId,
            TraceId = ResolveTraceId()
        }, ct).ConfigureAwait(false);

        _ = tenant;
        return await SendInviteAsync(invite, token, ct).ConfigureAwait(false);
    }

    private async Task<TenantInviteCreatedResult> ResendInviteCoreAsync(Guid tenantId, Guid inviteId, Guid resentByUserId, CancellationToken ct)
    {
        ValidateTenantAndUser(tenantId, resentByUserId);
        var invite = await RequireInviteAsync(tenantId, inviteId, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        invite = await ExpireIfNeededAsync(invite, now, ct).ConfigureAwait(false);
        if (!invite.IsRedeemable(now))
            throw new IdentityValidationException("Invite cannot be resent.");

        await PublishInviteEventAsync(new TenantInviteResendRequestedEvent
        {
            InviteId = invite.InviteId,
            TenantId = invite.TenantId,
            DestinationType = invite.DestinationType,
            NormalizedDestination = invite.NormalizedDestination,
            ResentByUserId = resentByUserId,
            CorrelationId = invite.CorrelationId,
            CausationId = invite.CausationId,
            TraceId = ResolveTraceId()
        }, ct).ConfigureAwait(false);

        var result = await SendInviteAsync(invite, CreateInviteToken(), ct).ConfigureAwait(false);

        await PublishInviteEventAsync(new TenantInviteResentEvent
        {
            InviteId = invite.InviteId,
            TenantId = invite.TenantId,
            DestinationType = invite.DestinationType,
            NormalizedDestination = invite.NormalizedDestination,
            ResentByUserId = resentByUserId,
            CorrelationId = invite.CorrelationId,
            CausationId = invite.CausationId,
            TraceId = ResolveTraceId()
        }, ct).ConfigureAwait(false);

        return result;
    }

    private async Task<TenantInviteInfo> RevokeInviteCoreAsync(Guid tenantId, Guid inviteId, Guid revokedByUserId, string? reason, CancellationToken ct)
    {
        ValidateTenantAndUser(tenantId, revokedByUserId);
        var invite = await RequireInviteAsync(tenantId, inviteId, ct).ConfigureAwait(false);
        if (string.Equals(invite.Status, TenantInviteStatuses.Redeemed, StringComparison.OrdinalIgnoreCase))
            throw new IdentityValidationException("Redeemed invites cannot be revoked.");

        await PublishInviteEventAsync(new TenantInviteRevokeRequestedEvent
        {
            InviteId = invite.InviteId,
            TenantId = invite.TenantId,
            DestinationType = invite.DestinationType,
            NormalizedDestination = invite.NormalizedDestination,
            RevokedByUserId = revokedByUserId,
            Reason = NormalizeOptional(reason),
            CorrelationId = invite.CorrelationId,
            CausationId = invite.CausationId,
            TraceId = ResolveTraceId()
        }, ct).ConfigureAwait(false);

        invite = invite with
        {
            Status = TenantInviteStatuses.Revoked,
            RevokedUtc = DateTimeOffset.UtcNow,
            RevokedByUserId = revokedByUserId,
            RevokedReason = NormalizeOptional(reason)
        };
        invite = await _invites.UpdateAsync(invite, ct).ConfigureAwait(false);

        await PublishInviteEventAsync(new TenantInviteRevokedEvent
        {
            InviteId = invite.InviteId,
            TenantId = invite.TenantId,
            DestinationType = invite.DestinationType,
            NormalizedDestination = invite.NormalizedDestination,
            RevokedByUserId = revokedByUserId,
            Reason = invite.RevokedReason,
            CorrelationId = invite.CorrelationId,
            CausationId = invite.CausationId,
            TraceId = ResolveTraceId()
        }, ct).ConfigureAwait(false);

        return TenantInviteInfo.FromRecord(invite);
    }

    private async Task<TenantInvitePreview> PreviewInviteCoreAsync(string tokenOrCode, CancellationToken ct)
    {
        var invite = await FindByTokenAsync(tokenOrCode, ct).ConfigureAwait(false);
        invite = await ExpireIfNeededAsync(invite, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        if (!invite.IsRedeemable(DateTimeOffset.UtcNow))
            throw new IdentityValidationException("Invite is not active.");

        return new TenantInvitePreview(
            invite.InviteId,
            invite.TenantId,
            invite.DestinationType,
            invite.NormalizedDestination,
            invite.ExpiresUtc,
            invite.Status,
            invite.ProfileHints,
            invite.RedirectUrl,
            invite.RequirePasswordSetup);
    }

    private async Task<TenantInviteAcceptResult> AcceptInviteCoreAsync(
        TenantInviteAcceptRequest request,
        Guid? authenticatedUserId,
        CancellationToken ct)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var invite = await FindByTokenAsync(request.InviteToken ?? request.InviteCode, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        invite = await ExpireIfNeededAsync(invite, now, ct).ConfigureAwait(false);
        if (!invite.IsRedeemable(now))
            throw new IdentityValidationException("Invite is not active.");

        var mode = NormalizeAcceptMode(request.Mode);
        await PublishInviteEventAsync(new TenantInviteAcceptRequestedEvent
        {
            InviteId = invite.InviteId,
            TenantId = invite.TenantId,
            DestinationType = invite.DestinationType,
            NormalizedDestination = invite.NormalizedDestination,
            Mode = mode,
            CorrelationId = request.CorrelationId ?? invite.CorrelationId,
            CausationId = request.CausationId ?? invite.CausationId,
            TraceId = ResolveTraceId()
        }, ct).ConfigureAwait(false);

        var (user, createdNewUser) = await ResolveAcceptedUserAsync(invite, request, mode, authenticatedUserId, ct)
            .ConfigureAwait(false);

        await PublishInviteEventAsync(new TenantInviteTenantUserLinkRequestedEvent
        {
            InviteId = invite.InviteId,
            TenantId = invite.TenantId,
            DestinationType = invite.DestinationType,
            NormalizedDestination = invite.NormalizedDestination,
            UserId = user.UserId,
            CorrelationId = request.CorrelationId ?? invite.CorrelationId,
            CausationId = request.CausationId ?? invite.CausationId,
            TraceId = ResolveTraceId()
        }, ct).ConfigureAwait(false);

        await PublishInviteEventAsync(new TenantInviteRoleAssignmentRequestedEvent
        {
            InviteId = invite.InviteId,
            TenantId = invite.TenantId,
            DestinationType = invite.DestinationType,
            NormalizedDestination = invite.NormalizedDestination,
            UserId = user.UserId,
            CorrelationId = request.CorrelationId ?? invite.CorrelationId,
            CausationId = request.CausationId ?? invite.CausationId,
            TraceId = ResolveTraceId()
        }, ct).ConfigureAwait(false);

        var assignment = await EnsureMembershipAndRolesAsync(invite, request, user, ct).ConfigureAwait(false);

        await _userExtensions.EnsureExtensionAsync(
            user with { DisplayName = FirstNonEmpty(request.DisplayName, invite.ProfileHints?.DisplayName, user.DisplayName) },
            UserExtensionContext.Create(
                "invite-accepted",
                user.UserId,
                invite.TenantId,
                user.Email,
                user.PhoneNumber,
                FirstNonEmpty(request.DisplayName, invite.ProfileHints?.DisplayName, user.DisplayName),
                FirstNonEmpty(request.FirstName, invite.ProfileHints?.FirstName),
                FirstNonEmpty(request.LastName, invite.ProfileHints?.LastName),
                request.CorrelationId ?? invite.CorrelationId,
                request.CausationId ?? invite.CausationId,
                ResolveTraceId(),
                MergeMetadata(invite.Metadata, invite.ProfileHints?.Metadata)),
            ct).ConfigureAwait(false);

        await PublishInviteEventAsync(new TenantInviteTenantUserLinkedEvent
        {
            InviteId = invite.InviteId,
            TenantId = invite.TenantId,
            DestinationType = invite.DestinationType,
            NormalizedDestination = invite.NormalizedDestination,
            UserId = user.UserId,
            CorrelationId = request.CorrelationId ?? invite.CorrelationId,
            CausationId = request.CausationId ?? invite.CausationId,
            TraceId = ResolveTraceId()
        }, ct).ConfigureAwait(false);

        await PublishInviteEventAsync(new TenantInviteRolesAssignedEvent
        {
            InviteId = invite.InviteId,
            TenantId = invite.TenantId,
            DestinationType = invite.DestinationType,
            NormalizedDestination = invite.NormalizedDestination,
            UserId = user.UserId,
            CorrelationId = request.CorrelationId ?? invite.CorrelationId,
            CausationId = request.CausationId ?? invite.CausationId,
            TraceId = ResolveTraceId()
        }, ct).ConfigureAwait(false);

        await ApplyAccessGrantsAsync(invite, user.UserId, ct).ConfigureAwait(false);

        invite = invite with
        {
            Status = TenantInviteStatuses.Redeemed,
            RedeemedUtc = DateTimeOffset.UtcNow,
            RedeemedByUserId = user.UserId
        };
        invite = await _invites.UpdateAsync(invite, ct).ConfigureAwait(false);

        await PublishInviteEventAsync(new TenantInviteAcceptedEvent
        {
            InviteId = invite.InviteId,
            TenantId = invite.TenantId,
            DestinationType = invite.DestinationType,
            NormalizedDestination = invite.NormalizedDestination,
            UserId = user.UserId,
            CreatedNewUser = createdNewUser,
            CorrelationId = request.CorrelationId ?? invite.CorrelationId,
            CausationId = request.CausationId ?? invite.CausationId,
            TraceId = ResolveTraceId()
        }, ct).ConfigureAwait(false);

        var membership = await _memberships.GetTenantForUserAsync(user.UserId, invite.TenantId, ct).ConfigureAwait(false)
            ?? new TenantInfo(invite.TenantId, string.Empty, assignment.Roles.Select(x => x.Name).ToList(), true, assignment.Roles.Select(x => x.RoleId).ToList());
        var token = await CreateTenantTokenAsync(user, membership, ct).ConfigureAwait(false);

        return new TenantInviteAcceptResult(
            TenantInviteInfo.FromRecord(invite),
            user,
            membership,
            token,
            createdNewUser,
            assignment.Roles);
    }

    private async Task<TenantInviteCreatedResult> SendInviteAsync(TenantInviteRecord invite, string inviteToken, CancellationToken ct)
    {
        var tokenHash = HashToken(inviteToken);
        invite = invite with { TokenHash = tokenHash };
        var url = _urlBuilder.BuildInviteUrl(invite, inviteToken);

        await PublishInviteEventAsync(new TenantInviteSendRequestedEvent
        {
            InviteId = invite.InviteId,
            TenantId = invite.TenantId,
            DestinationType = invite.DestinationType,
            NormalizedDestination = invite.NormalizedDestination,
            CorrelationId = invite.CorrelationId,
            CausationId = invite.CausationId,
            TraceId = ResolveTraceId()
        }, ct).ConfigureAwait(false);

        await _sender.SendAsync(_messageFactory.CreateMessage(invite, inviteToken, url), ct).ConfigureAwait(false);

        invite = await _invites.UpdateAsync(invite with
        {
            Status = TenantInviteStatuses.Sent,
            SentUtc = DateTimeOffset.UtcNow
        }, ct).ConfigureAwait(false);

        await PublishInviteEventAsync(new TenantInviteSentEvent
        {
            InviteId = invite.InviteId,
            TenantId = invite.TenantId,
            DestinationType = invite.DestinationType,
            NormalizedDestination = invite.NormalizedDestination,
            CorrelationId = invite.CorrelationId,
            CausationId = invite.CausationId,
            TraceId = ResolveTraceId()
        }, ct).ConfigureAwait(false);

        return new TenantInviteCreatedResult(TenantInviteInfo.FromRecord(invite), inviteToken, url);
    }

    private async Task<(IdentityUser User, bool CreatedNewUser)> ResolveAcceptedUserAsync(
        TenantInviteRecord invite,
        TenantInviteAcceptRequest request,
        string mode,
        Guid? authenticatedUserId,
        CancellationToken ct)
    {
        if (mode == TenantInviteAcceptModes.ExistingSession)
        {
            if (!authenticatedUserId.HasValue || authenticatedUserId.Value == Guid.Empty)
                throw new IdentityUnauthorizedException("Authenticated user is required.");

            var user = await _users.FindByIdAsync(authenticatedUserId.Value, ct).ConfigureAwait(false)
                ?? throw new IdentityUnauthorizedException("Authenticated user was not found.");
            EnsureUserOwnsVerifiedDestination(invite, user);
            return (user, false);
        }

        if (mode == TenantInviteAcceptModes.Otp || mode == TenantInviteAcceptModes.SmsOtp)
        {
            await VerifyOtpAsync(invite, request, ct).ConfigureAwait(false);
            return await FindOrCreateUserForInviteAsync(invite, request.Password, request.DisplayName, ct).ConfigureAwait(false);
        }

        if (mode == TenantInviteAcceptModes.EmailPassword)
        {
            if (invite.DestinationType != TenantInviteDestinationTypes.Email)
                throw new IdentityValidationException("email-password mode requires an email invite.");
            await VerifyEmailPasswordChallengeAsync(invite, request, ct).ConfigureAwait(false);

            var existing = await _users.FindByEmailAsync(invite.NormalizedDestination, ct).ConfigureAwait(false);
            if (existing is not null)
            {
                if (invite.RequirePasswordSetup)
                {
                    if (string.IsNullOrWhiteSpace(request.Password))
                        throw new IdentityValidationException("Password is required.");

                    await _users.SetPasswordAsync(existing.UserId, request.Password, ct).ConfigureAwait(false);
                    await _users.SetEmailConfirmedAsync(existing.UserId, true, ct).ConfigureAwait(false);
                    return (existing with { EmailConfirmed = true }, false);
                }

                if (string.IsNullOrWhiteSpace(request.Password) ||
                    !await _users.ValidatePasswordAsync(invite.NormalizedDestination, request.Password, ct).ConfigureAwait(false))
                {
                    throw new IdentityUnauthorizedException("Invalid credentials.");
                }

                return (existing, false);
            }

            if (string.IsNullOrWhiteSpace(request.Password))
                throw new IdentityValidationException("Password is required.");

            return await CreateUserForInviteAsync(invite, request.Password, request.DisplayName, ct).ConfigureAwait(false);
        }

        throw new IdentityValidationException("Invite acceptance mode is not supported.");
    }

    private async Task VerifyOtpAsync(TenantInviteRecord invite, TenantInviteAcceptRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ChallengeId))
            throw new IdentityValidationException("ChallengeId is required.");
        if (string.IsNullOrWhiteSpace(request.Code))
            throw new IdentityValidationException("Code is required.");

        var verify = await _otpService.VerifyAsync(new OtpVerifyRequest(request.ChallengeId, request.Code), ct)
            .ConfigureAwait(false);
        if (!verify.Success)
            throw new IdentityValidationException("OTP verification failed.");

        var challenge = await _otpChallenges.GetAsync(request.ChallengeId, ct).ConfigureAwait(false)
            ?? throw new IdentityValidationException("OTP challenge not found.");
        EnsureChallengeMatchesInvite(invite, challenge);
    }

    private async Task VerifyEmailPasswordChallengeAsync(TenantInviteRecord invite, TenantInviteAcceptRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ChallengeId))
            return;
        if (string.IsNullOrWhiteSpace(request.VerificationToken))
            throw new IdentityValidationException("VerificationToken is required.");

        var challenge = await _otpChallenges.GetAsync(request.ChallengeId, ct).ConfigureAwait(false)
            ?? throw new IdentityValidationException("OTP challenge not found.");
        EnsureChallengeMatchesInvite(invite, challenge);
        if (string.IsNullOrWhiteSpace(challenge.VerificationToken) ||
            !string.Equals(challenge.VerificationToken, request.VerificationToken, StringComparison.Ordinal) ||
            challenge.VerificationTokenExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new IdentityValidationException("Verification token is invalid.");
        }
    }

    private async Task<(IdentityUser User, bool CreatedNewUser)> FindOrCreateUserForInviteAsync(
        TenantInviteRecord invite,
        string? password,
        string? displayName,
        CancellationToken ct)
    {
        var existing = invite.DestinationType == TenantInviteDestinationTypes.Email
            ? await _users.FindByEmailAsync(invite.NormalizedDestination, ct).ConfigureAwait(false)
            : await _users.FindByPhoneAsync(invite.NormalizedDestination, ct).ConfigureAwait(false);

        if (existing is not null)
            return (existing, false);

        return await CreateUserForInviteAsync(invite, password ?? string.Empty, displayName, ct).ConfigureAwait(false);
    }

    private async Task<(IdentityUser User, bool CreatedNewUser)> CreateUserForInviteAsync(
        TenantInviteRecord invite,
        string password,
        string? displayName,
        CancellationToken ct)
    {
        var create = invite.DestinationType == TenantInviteDestinationTypes.Email
            ? new RegisterUserRequest(invite.NormalizedDestination, null, password, FirstNonEmpty(displayName, invite.ProfileHints?.DisplayName))
            : new RegisterUserRequest(null, invite.NormalizedDestination, password, FirstNonEmpty(displayName, invite.ProfileHints?.DisplayName));

        var result = await _users.CreateAsync(create, ct).ConfigureAwait(false);
        if (!result.Succeeded || result.User is null)
            throw new IdentityValidationException("Registration failed.", result.Errors);

        if (invite.DestinationType == TenantInviteDestinationTypes.Email)
            await _users.SetEmailConfirmedAsync(result.User.UserId, true, ct).ConfigureAwait(false);
        else
            await _users.SetPhoneConfirmedAsync(result.User.UserId, true, ct).ConfigureAwait(false);

        var user = await _users.FindByIdAsync(result.User.UserId, ct).ConfigureAwait(false) ?? result.User;
        return (user, true);
    }

    private async Task ApplyAccessGrantsAsync(TenantInviteRecord invite, Guid userId, CancellationToken ct)
    {
        var grants = invite.AccessGrants ?? [];
        if (grants.Count == 0)
            return;
        if (_resourceAccess is null)
            return;

        await PublishInviteEventAsync(new TenantInviteAccessGrantAssignmentRequestedEvent
        {
            InviteId = invite.InviteId,
            TenantId = invite.TenantId,
            DestinationType = invite.DestinationType,
            NormalizedDestination = invite.NormalizedDestination,
            UserId = userId,
            GrantCount = grants.Count,
            CorrelationId = invite.CorrelationId,
            CausationId = invite.CausationId,
            TraceId = ResolveTraceId()
        }, ct).ConfigureAwait(false);

        foreach (var grant in grants)
        {
            await _resourceAccess.GrantAccessAsync(
                invite.TenantId,
                new GrantResourceAccessRequest
                {
                    ResourceType = grant.ResourceType,
                    ResourceId = grant.ResourceId,
                    AccessLevel = grant.AccessLevel,
                    ExpiresUtc = grant.ExpirationUtc,
                    Subject = new AccessSubject(IBeam.AccessControl.AccessSubjectTypes.User, userId.ToString("D")),
                    Metadata = grant.Metadata?.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase) ?? []
                },
                invite.InvitedByUserId,
                ct).ConfigureAwait(false);
        }

        await PublishInviteEventAsync(new TenantInviteAccessGrantsAssignedEvent
        {
            InviteId = invite.InviteId,
            TenantId = invite.TenantId,
            DestinationType = invite.DestinationType,
            NormalizedDestination = invite.NormalizedDestination,
            UserId = userId,
            GrantCount = grants.Count,
            CorrelationId = invite.CorrelationId,
            CausationId = invite.CausationId,
            TraceId = ResolveTraceId()
        }, ct).ConfigureAwait(false);
    }

    private async Task<TokenResult> CreateTenantTokenAsync(IdentityUser user, TenantInfo tenant, CancellationToken ct)
    {
        var claims = new List<ClaimItem>
        {
            new("sub", user.UserId.ToString("D")),
            new("uid", user.UserId.ToString("D")),
            new("tid", tenant.TenantId.ToString("D"))
        };

        if (!string.IsNullOrWhiteSpace(user.Email))
            claims.Add(new("email", user.Email));
        if (!string.IsNullOrWhiteSpace(user.PhoneNumber))
            claims.Add(new("phone_number", user.PhoneNumber!));
        foreach (var role in tenant.Roles.Where(x => !string.IsNullOrWhiteSpace(x)))
            claims.Add(new("role", role));
        foreach (var roleId in tenant.RoleIds?.Where(x => x != Guid.Empty).Distinct() ?? [])
            claims.Add(new("rid", roleId.ToString("D")));

        return await _tokens.CreateAccessTokenAsync(user.UserId, tenant.TenantId, claims, ct).ConfigureAwait(false);
    }

    private async Task<UserTenantRoleAssignment> EnsureMembershipAndRolesAsync(
        TenantInviteRecord invite,
        TenantInviteAcceptRequest request,
        IdentityUser user,
        CancellationToken ct)
    {
        var roleIds = invite.RoleIds?.Where(x => x != Guid.Empty).Distinct().ToList() ?? [];
        var roleNames = invite.RoleNames?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        var displayName = FirstNonEmpty(request.DisplayName, invite.ProfileHints?.DisplayName, user.DisplayName);
        var setAsDefault = request.SetAsDefaultTenant ?? invite.SetAsDefaultTenant;

        if (roleIds.Count > 0 || roleNames.Count > 0)
        {
            return await _roles.EnsureTenantMembershipAndGrantRolesAsync(
                new TenantMembershipRoleBootstrapRequest(
                    invite.TenantId,
                    user.UserId,
                    RoleIds: roleIds,
                    RoleNames: roleNames,
                    SetAsDefault: setAsDefault,
                    UserDisplayName: displayName,
                    UserEmail: user.Email,
                    UserPhoneNumber: user.PhoneNumber),
                ct).ConfigureAwait(false);
        }

        await _tenantProvisioning.EnsureUserTenantMembershipAsync(
            invite.TenantId,
            user.UserId,
            roleNames: null,
            setAsDefault: setAsDefault,
            ct: ct).ConfigureAwait(false);

        var roles = await _roles.GetRolesForUserAsync(invite.TenantId, user.UserId, ct).ConfigureAwait(false);
        return new UserTenantRoleAssignment(invite.TenantId, user.UserId, roles);
    }

    private static void EnsureUserOwnsVerifiedDestination(TenantInviteRecord invite, IdentityUser user)
    {
        if (invite.DestinationType == TenantInviteDestinationTypes.Email)
        {
            var email = NormalizeEmail(user.Email);
            if (!user.EmailConfirmed || !string.Equals(email, invite.NormalizedDestination, StringComparison.OrdinalIgnoreCase))
                throw new IdentityUnauthorizedException("Authenticated user does not match invite recipient.");
            return;
        }

        var phone = NormalizePhone(user.PhoneNumber);
        if (!user.PhoneConfirmed || !string.Equals(phone, invite.NormalizedDestination, StringComparison.OrdinalIgnoreCase))
            throw new IdentityUnauthorizedException("Authenticated user does not match invite recipient.");
    }

    private static void EnsureChallengeMatchesInvite(TenantInviteRecord invite, OtpChallengeRecord challenge)
    {
        var destination = invite.DestinationType == TenantInviteDestinationTypes.Email
            ? NormalizeEmail(challenge.Destination)
            : NormalizePhone(challenge.Destination);

        if (!string.Equals(destination, invite.NormalizedDestination, StringComparison.OrdinalIgnoreCase))
            throw new IdentityValidationException("OTP destination mismatch.");
    }

    private async Task<TenantInviteRecord> RequireInviteAsync(Guid tenantId, Guid inviteId, CancellationToken ct)
        => await _invites.GetAsync(tenantId, inviteId, ct).ConfigureAwait(false)
           ?? throw new IdentityValidationException("Invite not found.");

    private async Task<TenantInviteRecord> FindByTokenAsync(string? tokenOrCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tokenOrCode))
            throw new IdentityValidationException("InviteToken or InviteCode is required.");

        return await _invites.FindByTokenHashAsync(HashToken(tokenOrCode.Trim()), ct).ConfigureAwait(false)
               ?? throw new IdentityValidationException("Invite not found.");
    }

    private async Task<TenantInviteRecord> ExpireIfNeededAsync(TenantInviteRecord invite, DateTimeOffset now, CancellationToken ct)
    {
        if (invite.ExpiresUtc > now ||
            !string.Equals(invite.Status, TenantInviteStatuses.Pending, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(invite.Status, TenantInviteStatuses.Sent, StringComparison.OrdinalIgnoreCase))
        {
            return invite;
        }

        await PublishInviteEventAsync(new TenantInviteExpirationRequestedEvent
        {
            InviteId = invite.InviteId,
            TenantId = invite.TenantId,
            DestinationType = invite.DestinationType,
            NormalizedDestination = invite.NormalizedDestination,
            CorrelationId = invite.CorrelationId,
            CausationId = invite.CausationId,
            TraceId = ResolveTraceId()
        }, ct).ConfigureAwait(false);

        invite = await _invites.UpdateAsync(invite with { Status = TenantInviteStatuses.Expired }, ct)
            .ConfigureAwait(false);

        await PublishInviteEventAsync(new TenantInviteExpiredEvent
        {
            InviteId = invite.InviteId,
            TenantId = invite.TenantId,
            DestinationType = invite.DestinationType,
            NormalizedDestination = invite.NormalizedDestination,
            CorrelationId = invite.CorrelationId,
            CausationId = invite.CausationId,
            TraceId = ResolveTraceId()
        }, ct).ConfigureAwait(false);

        return invite;
    }

    private Task PublishInviteEventAsync<TEvent>(TEvent evt, CancellationToken ct)
        where TEvent : TenantInviteLifecycleEventBase
    {
        evt.Metadata["idempotencyKey"] = $"{evt.EventType}:{evt.InviteId:D}";
        return PublishEventCoreAsync(evt, ct);
    }

    private async Task PublishEventCoreAsync<TEvent>(TEvent evt, CancellationToken ct)
        where TEvent : AuthLifecycleEventBase
    {
        try
        {
            await _events.PublishAsync(evt, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to publish invite lifecycle event {EventType}.", evt.EventType);
            if (_eventOptions.Value.StrictPublishFailures)
                throw;
        }
    }

    private static (string DestinationType, string NormalizedDestination) NormalizeDestination(TenantInviteCreateRequest request)
    {
        var type = (request.DestinationType ?? string.Empty).Trim().ToLowerInvariant();
        if (type == TenantInviteDestinationTypes.Email)
        {
            if (string.IsNullOrWhiteSpace(request.Email))
                throw new IdentityValidationException("Email is required.");
            return (type, NormalizeEmail(request.Email));
        }

        if (type == TenantInviteDestinationTypes.Sms)
        {
            if (string.IsNullOrWhiteSpace(request.PhoneNumber))
                throw new IdentityValidationException("PhoneNumber is required.");
            return (type, NormalizePhone(request.PhoneNumber));
        }

        throw new IdentityValidationException("DestinationType must be 'email' or 'sms'.");
    }

    private static string NormalizeAcceptMode(string? mode)
    {
        var value = string.IsNullOrWhiteSpace(mode) ? TenantInviteAcceptModes.Otp : mode.Trim().ToLowerInvariant();
        return value switch
        {
            TenantInviteAcceptModes.Otp => TenantInviteAcceptModes.Otp,
            TenantInviteAcceptModes.EmailPassword => TenantInviteAcceptModes.EmailPassword,
            TenantInviteAcceptModes.SmsOtp => TenantInviteAcceptModes.SmsOtp,
            TenantInviteAcceptModes.ExistingSession => TenantInviteAcceptModes.ExistingSession,
            _ => throw new IdentityValidationException("Mode must be 'otp', 'email-password', 'sms-otp', or 'existing-session'.")
        };
    }

    private static IReadOnlyList<Guid> NormalizeRoleIds(IReadOnlyList<Guid>? roleIds)
        => roleIds?.Where(x => x != Guid.Empty).Distinct().ToList() ?? [];

    private static IReadOnlyList<string> NormalizeRoleNames(IReadOnlyList<string>? roleNames)
        => roleNames?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

    private static IReadOnlyList<TenantInviteAccessGrantRequest> NormalizeAccessGrants(IReadOnlyList<TenantInviteAccessGrantRequest>? grants)
        => grants?
            .Select(x => x with
            {
                ResourceType = NormalizeRequired(x.ResourceType, "ResourceType"),
                ResourceId = NormalizeRequired(x.ResourceId, "ResourceId"),
                AccessLevel = NormalizeRequired(x.AccessLevel, "AccessLevel"),
                Metadata = NormalizeMetadata(x.Metadata)
            })
            .ToList() ?? [];

    private static IReadOnlyList<TenantInviteInfo> ApplyListFilter(
        IReadOnlyList<TenantInviteInfo> invites,
        TenantInviteListRequest? request,
        DateTimeOffset now)
    {
        if (request is null)
            return invites;

        var status = NormalizeOptional(request.Status)?.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(status) && !IsKnownStatus(status))
            throw new IdentityValidationException("Status must be pending, sent, expired, redeemed, or revoked.");

        var filtered = invites.Select(x => x with { Status = ResolveEffectiveStatus(x, now) });

        if (request.ActiveOnly)
        {
            filtered = filtered.Where(IsActiveInvite);
        }
        else
        {
            if (request.IncludeExpired == false)
                filtered = filtered.Where(x => !string.Equals(x.Status, TenantInviteStatuses.Expired, StringComparison.OrdinalIgnoreCase));
            if (request.IncludeRedeemed == false)
                filtered = filtered.Where(x => !string.Equals(x.Status, TenantInviteStatuses.Redeemed, StringComparison.OrdinalIgnoreCase));
            if (request.IncludeRevoked == false)
                filtered = filtered.Where(x => !string.Equals(x.Status, TenantInviteStatuses.Revoked, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(status))
            filtered = filtered.Where(x => string.Equals(x.Status, status, StringComparison.OrdinalIgnoreCase));

        return filtered.ToList();
    }

    private static string ResolveEffectiveStatus(TenantInviteInfo invite, DateTimeOffset now)
    {
        if (invite.ExpiresUtc <= now &&
            (string.Equals(invite.Status, TenantInviteStatuses.Pending, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(invite.Status, TenantInviteStatuses.Sent, StringComparison.OrdinalIgnoreCase)))
        {
            return TenantInviteStatuses.Expired;
        }

        return invite.Status;
    }

    private static bool IsActiveInvite(TenantInviteInfo invite)
        => string.Equals(invite.Status, TenantInviteStatuses.Pending, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(invite.Status, TenantInviteStatuses.Sent, StringComparison.OrdinalIgnoreCase);

    private static bool IsKnownStatus(string status)
        => string.Equals(status, TenantInviteStatuses.Pending, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(status, TenantInviteStatuses.Sent, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(status, TenantInviteStatuses.Expired, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(status, TenantInviteStatuses.Redeemed, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(status, TenantInviteStatuses.Revoked, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, string> NormalizeMetadata(IReadOnlyDictionary<string, string>? metadata)
        => metadata?
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && kv.Value is not null)
            .ToDictionary(kv => kv.Key.Trim(), kv => kv.Value.Trim(), StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, string>();

    private static IReadOnlyDictionary<string, string> MergeMetadata(params IReadOnlyDictionary<string, string>?[] metadata)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in metadata)
        {
            if (source is null)
                continue;
            foreach (var kv in source)
                merged[kv.Key] = kv.Value;
        }

        return merged;
    }

    private static string NormalizeRequired(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new IdentityValidationException($"{name} is required.");
        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || !IdentityUtils.EmailRegex.IsMatch(email.Trim()))
            throw new IdentityValidationException("Email must be valid.");
        return email.Trim().ToLowerInvariant();
    }

    private static string NormalizePhone(string? phone)
        => IdentityUtils.NormalizePhoneNumber(phone);

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();

    private static void ValidateTenantAndUser(Guid tenantId, Guid userId)
    {
        if (tenantId == Guid.Empty)
            throw new IdentityValidationException("tenantId is required.");
        if (userId == Guid.Empty)
            throw new IdentityValidationException("userId is required.");
    }

    private static string CreateInviteToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim()));
        return Convert.ToHexString(bytes);
    }

    private static string? ResolveTraceId()
    {
        var current = Activity.Current;
        if (current is null)
            return null;

        return current.TraceId != default ? current.TraceId.ToString() : current.Id;
    }
}
