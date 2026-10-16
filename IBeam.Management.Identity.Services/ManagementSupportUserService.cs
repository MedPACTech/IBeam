using System.Net.Mail;
using IBeam.AccessControl;
using System.Security.Cryptography;
using System.Text;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Services.Abstractions;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Identity.Services;

/// <summary>Management Identity adapter. Reuses existing directory/invite delivery and Identity stores; never issues an onboarding token.</summary>
public sealed class ManagementSupportUserService(IManagementPrincipalProvider principals,
    ManagementIdentitySessionService sessions, ITenantUserDirectoryService directory, ManagementSupportInvitationDelivery invites,
    ITenantInviteStore inviteStore, IIdentityUserStore users, ITenantMembershipStore memberships, ITenantRoleService roles,
    IManagementInvitationRedemptionStore redemptions, ManagementSupportAccessService disabling, IManagementSupportUserStateProvider states,
    IServiceOperationSystemContext system, IServiceOperationExecutor operations, IOptions<ManagementIdentityOptions> options)
    : IManagementSupportUserService
{
    public Task<IReadOnlyList<ManagementSupportUserDto>> ListAsync(CancellationToken ct = default)
        => Run<IReadOnlyList<ManagementSupportUserDto>>("management.support-users.list", null, async token =>
        {
            await RequireAdministratorAsync(token);
            using var trusted = system.Enter("management.support-users.directory");
            var rows = await directory.ListAsync(options.Value.OrganizationId, new(IncludeDisabled: true), token);
            if (rows.Any(x => x.TenantId != options.Value.OrganizationId)) throw new ManagementSupportUnavailableException();
            return rows.Where(x => x.UserId.HasValue).Select(x => new ManagementSupportUserDto(x.UserId!.Value, x.Email,
                x.Status == TenantUserDirectoryStatuses.Active, x.RoleNames?.Contains(ManagementIdentityRoles.Administrator, StringComparer.Ordinal) == true)).ToArray();
        }, ct);

    public Task<ManagementSupportInvitationDto> InviteAsync(ManagementSupportInvitationRequest request, CancellationToken ct = default)
        => Run("management.support-invitations.create", null, async token =>
        {
            var actor = await RequireAdministratorAsync(token);
            if (request is null || string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 254 || !MailAddress.TryCreate(request.Email, out var email) ||
                !string.Equals(email.Address, request.Email, StringComparison.OrdinalIgnoreCase)) throw new ManagementSupportInvalidException();
            var existing = await users.FindByEmailAsync(email.Address, token);
            if (existing is not null && await memberships.GetTenantForUserAsync(existing.UserId, options.Value.OrganizationId, token) is not null)
                throw new ManagementSupportInvalidException();
            using var trusted = system.Enter("management.support-invitations.create");
            var catalog = await roles.GetRolesAsync(options.Value.OrganizationId, token);
            var role = catalog.SingleOrDefault(x => x.Name == ManagementIdentityRoles.Support)
                ?? await roles.CreateRoleAsync(options.Value.OrganizationId, ManagementIdentityRoles.Support, token);
            if (!role.IsActive) throw new ManagementAccessDeniedException();
            var result = await invites.CreateAsync(options.Value.OrganizationId,
                new(TenantInviteDestinationTypes.Email, Email: email.Address, RoleIds: [role.RoleId],
                    ExpiresUtc: DateTimeOffset.UtcNow.AddDays(2), Metadata: new Dictionary<string, string> { ["managementSupport"] = "v1" }),
                actor.User.UserId, token);
            return new ManagementSupportInvitationDto(result.Invite.InviteId, result.Invite.ExpiresUtc, result.Invite.Status);
        }, ct);

    public Task<ManagementSupportInvitationAccepted> AcceptAsync(ManagementSupportInvitationAcceptRequest request, CancellationToken ct = default)
        => Run("management.support-invitations.accept", null, async token =>
        {
            if (request is null || request.InviteToken is not { Length: >= 16 and <= 4096 } ||
                request.Password is not { Length: >= 8 and <= 1024 }) throw new ManagementSupportInvalidException();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.InviteToken.Trim())));
            var indexed = await inviteStore.FindByTokenHashAsync(hash, token) ?? throw new ManagementSupportInvalidException();
            var org = options.Value.OrganizationId;
            if (indexed.TenantId != org) throw new ManagementSupportInvalidException();
            var invite = await inviteStore.GetAsync(org, indexed.InviteId, token) ?? throw new ManagementSupportInvalidException();
            if (invite.TokenHash != hash || !invite.IsRedeemable(DateTimeOffset.UtcNow) ||
                invite.DestinationType != TenantInviteDestinationTypes.Email ||
                invite.Metadata?.GetValueOrDefault("managementSupport") != "v1" ||
                invite.AccessGrants?.Count > 0 || invite.RoleNames?.Count > 0 || invite.RoleIds?.Count != 1)
                throw new ManagementSupportInvalidException();
            var inviter = await states.GetAsync(org, invite.InvitedByUserId, token);
            if (inviter is not { IsEnabled: true, IsAdministrator: true }) throw new ManagementSupportInvalidException();
            using var trusted = system.Enter("management.support-invitations.accept");
            var catalog = await roles.GetRolesAsync(org, token);
            var support = catalog.SingleOrDefault(x => x.Name == ManagementIdentityRoles.Support && x.IsActive);
            if (support is null || invite.RoleIds[0] != support.RoleId) throw new ManagementSupportInvalidException();
            var user = await users.FindByEmailAsync(invite.NormalizedDestination, token);
            if (user is not null)
            {
                if (!await users.ValidatePasswordAsync(invite.NormalizedDestination, request.Password, token) ||
                    await memberships.GetTenantForUserAsync(user.UserId, org, token) is not null)
                    throw new ManagementSupportInvalidException();
            }
            else
            {
                var created = await users.CreateAsync(new RegisterUserRequest(invite.NormalizedDestination, null, request.Password), token);
                if (!created.Succeeded || created.User is null) throw new ManagementSupportInvalidException();
                user = created.User;
            }
            if (!await redemptions.TryClaimAsync(org, invite.InviteId, user.UserId, token)) throw new ManagementSupportInvalidException();
            // The email-delivered capability proves the intended destination. No submitted email/org/roles are accepted.
            await users.SetEmailConfirmedAsync(user.UserId, true, token);
            await users.SetTwoFactorAsync(user.UserId, true, "email", token);
            await roles.EnsureTenantMembershipAndGrantRolesAsync(new(org, user.UserId, RoleIds: [support.RoleId],
                SetAsDefault: true, UserEmail: invite.NormalizedDestination), token);
            await inviteStore.UpdateAsync(invite with { Status = TenantInviteStatuses.Redeemed,
                RedeemedUtc = DateTimeOffset.UtcNow, RedeemedByUserId = user.UserId }, token);
            return new ManagementSupportInvitationAccepted(user.UserId);
        }, ct);

    public Task DisableAsync(Guid userId, CancellationToken ct = default) => UpdateAsync(userId, false, ct);
    public Task UpdateAsync(Guid userId, bool isEnabled, CancellationToken ct = default)
        => Run("management.support-users.disable", userId, async token =>
        {
            await RequireAdministratorAsync(token);
            if (isEnabled) throw new ManagementSupportInvalidException();
            await disabling.DisableAsync(principals.GetPrincipal(), userId, token);
            return true;
        }, ct);

    private async Task<ManagementLiveMember> RequireAdministratorAsync(CancellationToken ct)
    {
        var principal = principals.GetPrincipal();
        var member = await sessions.ValidateAccessAsync(principal, ct);
        if (!principal.HasClaim(ManagementClaimTypes.Permission, ManagementSupportOperations.UsersAdmin) ||
            !member.Membership.Roles.Contains(ManagementIdentityRoles.Administrator, StringComparer.Ordinal)) throw new ManagementAccessDeniedException();
        return member;
    }
    private Task<T> Run<T>(string operation, Guid? id, Func<CancellationToken, Task<T>> action, CancellationToken ct)
        => operations.ExecuteAsync(this, async token =>
        {
            try { return await action(token); }
            catch (Exception ex) when (ex is not (ManagementAccessDeniedException or ManagementSupportInvalidException or ManagementSupportUnavailableException or OperationCanceledException))
            { throw new ManagementSupportUnavailableException(); }
        }, new ServiceOperationExecutionOptions { OperationName = operation, AuditAction = operation, TenantId = options.Value.OrganizationId,
            EntityId = id, PermissionEnabled = false, AuditEnabled = true }, ct);
}
