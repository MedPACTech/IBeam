using System.Diagnostics;
using IBeam.AccessControl;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Services.Utils;
using IBeam.Services.Abstractions;

namespace IBeam.Identity.Services.Tenants;

[IBeamOperation("identity.tenantusers.provision")]
public sealed class TenantUserProvisioningService : ITenantUserProvisioningService
{
    private readonly IIdentityTenantService _tenants;
    private readonly IIdentityUserStore _users;
    private readonly ITenantRoleService _roles;
    private readonly ITenantProvisioningService _tenantProvisioning;
    private readonly ITenantMembershipStore _memberships;
    private readonly IIdentityUserExtensionCoordinator _userExtensions;
    private readonly ITenantInviteService _invites;
    private readonly IResourceAccessService? _resourceAccess;
    private readonly IServiceOperationExecutor _operations;

    public TenantUserProvisioningService(
        IIdentityTenantService tenants,
        IIdentityUserStore users,
        ITenantRoleService roles,
        ITenantProvisioningService tenantProvisioning,
        ITenantMembershipStore memberships,
        IIdentityUserExtensionCoordinator userExtensions,
        ITenantInviteService invites,
        IResourceAccessService? resourceAccess = null,
        IServiceOperationExecutor? operations = null)
    {
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _roles = roles ?? throw new ArgumentNullException(nameof(roles));
        _tenantProvisioning = tenantProvisioning ?? throw new ArgumentNullException(nameof(tenantProvisioning));
        _memberships = memberships ?? throw new ArgumentNullException(nameof(memberships));
        _userExtensions = userExtensions ?? throw new ArgumentNullException(nameof(userExtensions));
        _invites = invites ?? throw new ArgumentNullException(nameof(invites));
        _resourceAccess = resourceAccess;
        _operations = operations ?? new ServiceOperationExecutor();
    }

    [IBeamOperation("identity.tenantusers.provision.create")]
    public Task<ProvisionTenantUserResult> ProvisionAsync(
        Guid tenantId,
        ProvisionTenantUserRequest request,
        Guid provisionedByUserId,
        CancellationToken ct = default)
        => _operations.ExecuteAsync(
            this,
            token => ProvisionCoreAsync(tenantId, request, provisionedByUserId, token),
            new ServiceOperationExecutionOptions { TenantId = tenantId, EntityId = provisionedByUserId },
            ct);

    private async Task<ProvisionTenantUserResult> ProvisionCoreAsync(
        Guid tenantId,
        ProvisionTenantUserRequest request,
        Guid provisionedByUserId,
        CancellationToken ct)
    {
        if (tenantId == Guid.Empty)
            throw new IdentityValidationException("tenantId is required.");
        if (provisionedByUserId == Guid.Empty)
            throw new IdentityValidationException("provisionedByUserId is required.");
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        _ = await _tenants.FindByIdAsync(tenantId, ct).ConfigureAwait(false)
            ?? throw new IdentityValidationException("Tenant not found.");

        var email = NormalizeEmail(request.Email);
        var phone = NormalizePhone(request.PhoneNumber);
        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(phone))
            throw new IdentityValidationException("Email or phoneNumber is required.");

        var (user, createdNewUser) = await FindOrCreateUserAsync(request, email, phone, ct).ConfigureAwait(false);
        user = user with { DisplayName = FirstNonEmpty(request.DisplayName, user.DisplayName, email, phone) };

        var roles = await EnsureMembershipAndRolesAsync(tenantId, user, request, ct).ConfigureAwait(false);
        await ApplyAccessGrantsAsync(tenantId, user.UserId, provisionedByUserId, request.AccessGrants, ct).ConfigureAwait(false);

        await _userExtensions.EnsureExtensionAsync(
            user,
            UserExtensionContext.Create(
                UserExtensionOperations.AdminProvisioned,
                user.UserId,
                tenantId,
                email ?? user.Email,
                phone ?? user.PhoneNumber,
                request.DisplayName ?? user.DisplayName,
                request.FirstName,
                request.LastName,
                request.CorrelationId,
                request.CausationId,
                ResolveTraceId(),
                NormalizeMetadata(request.ProfileMetadata)),
            ct).ConfigureAwait(false);

        var membership = await _memberships.GetTenantForUserAsync(user.UserId, tenantId, ct).ConfigureAwait(false)
            ?? new TenantInfo(tenantId, string.Empty, roles.Select(x => x.Name).ToList(), true, roles.Select(x => x.RoleId).ToList());

        TenantInviteInfo? invite = null;
        if (request.SendInvite)
        {
            invite = (await _invites.CreateInviteAsync(
                    tenantId,
                    CreateSetupInviteRequest(request, email, phone),
                    provisionedByUserId,
                    ct)
                .ConfigureAwait(false)).Invite;
        }

        return new ProvisionTenantUserResult(user, membership, roles, createdNewUser, invite);
    }

    private async Task<(IdentityUser User, bool CreatedNewUser)> FindOrCreateUserAsync(
        ProvisionTenantUserRequest request,
        string? email,
        string? phone,
        CancellationToken ct)
    {
        IdentityUser? existing = null;
        if (!string.IsNullOrWhiteSpace(email))
            existing = await _users.FindByEmailAsync(email, ct).ConfigureAwait(false);
        if (existing is null && !string.IsNullOrWhiteSpace(phone))
            existing = await _users.FindByPhoneAsync(phone, ct).ConfigureAwait(false);
        if (existing is not null)
            return (existing, false);

        var created = await _users.CreateAsync(
                new RegisterUserRequest(
                    email,
                    phone,
                    Password: string.Empty,
                    DisplayName: FirstNonEmpty(request.DisplayName, email, phone)),
                ct)
            .ConfigureAwait(false);

        if (!created.Succeeded || created.User is null)
            throw new IdentityValidationException("Registration failed.", created.Errors);

        var user = await _users.FindByIdAsync(created.User.UserId, ct).ConfigureAwait(false) ?? created.User;
        return (user, true);
    }

    private async Task<IReadOnlyList<TenantRole>> EnsureMembershipAndRolesAsync(
        Guid tenantId,
        IdentityUser user,
        ProvisionTenantUserRequest request,
        CancellationToken ct)
    {
        var roleIds = NormalizeRoleIds(request.RoleIds);
        var roleNames = NormalizeRoleNames(request.RoleNames);

        if (roleIds.Count > 0 || roleNames.Count > 0)
        {
            var assignment = await _roles.EnsureTenantMembershipAndGrantRolesAsync(
                    new TenantMembershipRoleBootstrapRequest(
                        tenantId,
                        user.UserId,
                        RoleIds: roleIds,
                        RoleNames: roleNames,
                        SetAsDefault: request.SetAsDefaultTenant,
                        UserDisplayName: request.DisplayName ?? user.DisplayName,
                        UserEmail: user.Email,
                        UserPhoneNumber: user.PhoneNumber),
                    ct)
                .ConfigureAwait(false);

            return assignment.Roles;
        }

        await _tenantProvisioning.EnsureUserTenantMembershipAsync(
                tenantId,
                user.UserId,
                setAsDefault: request.SetAsDefaultTenant,
                ct: ct)
            .ConfigureAwait(false);

        return await _roles.GetRolesForUserAsync(tenantId, user.UserId, ct).ConfigureAwait(false);
    }

    private async Task ApplyAccessGrantsAsync(
        Guid tenantId,
        Guid userId,
        Guid provisionedByUserId,
        IReadOnlyList<TenantInviteAccessGrantRequest>? grants,
        CancellationToken ct)
    {
        if (_resourceAccess is null || grants is null || grants.Count == 0)
            return;

        foreach (var grant in NormalizeAccessGrants(grants))
        {
            await _resourceAccess.GrantAccessAsync(
                    tenantId,
                    new GrantResourceAccessRequest
                    {
                        ResourceType = grant.ResourceType,
                        ResourceId = grant.ResourceId,
                        AccessLevel = grant.AccessLevel,
                        ExpiresUtc = grant.ExpirationUtc,
                        Subject = new AccessSubject(IBeam.AccessControl.AccessSubjectTypes.User, userId.ToString("D")),
                        Metadata = grant.Metadata?.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase) ?? []
                    },
                    provisionedByUserId,
                    ct)
                .ConfigureAwait(false);
        }
    }

    private static TenantInviteCreateRequest CreateSetupInviteRequest(
        ProvisionTenantUserRequest request,
        string? email,
        string? phone)
    {
        var destinationType = !string.IsNullOrWhiteSpace(email)
            ? TenantInviteDestinationTypes.Email
            : TenantInviteDestinationTypes.Sms;

        return new TenantInviteCreateRequest(
            destinationType,
            Email: email,
            PhoneNumber: phone,
            DisplayName: request.DisplayName,
            FirstName: request.FirstName,
            LastName: request.LastName,
            RoleIds: NormalizeRoleIds(request.RoleIds),
            RoleNames: NormalizeRoleNames(request.RoleNames),
            SetAsDefaultTenant: request.SetAsDefaultTenant,
            RedirectUrl: request.RedirectUrl,
            Metadata: NormalizeMetadata(request.ProfileMetadata),
            CorrelationId: request.CorrelationId,
            CausationId: request.CausationId,
            RequirePasswordSetup: request.RequirePasswordSetup);
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

    private static IReadOnlyDictionary<string, string> NormalizeMetadata(IReadOnlyDictionary<string, string>? metadata)
        => metadata?
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && kv.Value is not null)
            .ToDictionary(kv => kv.Key.Trim(), kv => kv.Value.Trim(), StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, string>();

    private static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;
        if (!IdentityUtils.EmailRegex.IsMatch(email.Trim()))
            throw new IdentityValidationException("Email must be valid.");
        return email.Trim().ToLowerInvariant();
    }

    private static string? NormalizePhone(string? phone)
        => string.IsNullOrWhiteSpace(phone) ? null : IdentityUtils.NormalizePhoneNumber(phone);

    private static string NormalizeRequired(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new IdentityValidationException($"{name} is required.");
        return value.Trim();
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();

    private static string? ResolveTraceId()
    {
        var current = Activity.Current;
        if (current is null)
            return null;

        return current.TraceId != default ? current.TraceId.ToString() : current.Id;
    }
}
