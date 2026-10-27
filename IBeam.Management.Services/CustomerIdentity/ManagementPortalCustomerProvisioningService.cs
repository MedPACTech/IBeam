using System.Net.Mail;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Services.CustomerIdentity.Delegation;
namespace IBeam.Management.Services.CustomerIdentity;

public sealed class ManagementPortalCustomerProvisioningService(PortalMutationRuntime runtime, PortalMutationReferences references,
    IManagementCustomerIdentityClient client) : IManagementPortalCustomerProvisioningService
{
    public Task<IReadOnlyList<ManagementAssignableRoleDto>> ListRolesAsync(Guid app, Guid tenant, CancellationToken ct = default)
        => runtime.Run(this, app, "management.portal.customer-identity.roles.catalog", async token => {
            const string authority = ManagementCustomerIdentityOperations.RolesRead;
            await runtime.Authorize(app, authority, token);
            if (tenant == Guid.Empty) throw new ManagementCustomerIdentityInvalidRequestException();
            var connection = await runtime.Resolve(app, authority, token); IReadOnlyList<ManagementAssignableRoleDto> result;
            using (var attempt = runtime.Attempt(token)) result = await client.ListAssignableRolesAsync(connection, tenant, attempt.Token);
            await runtime.Recheck(connection, authority, token); return result;
        }, ct);
    public Task<ManagementPortalCustomerInvitationResult> ProvisionAsync(Guid app, Guid tenant, ProvisionManagementCustomerUser request, string key, CancellationToken ct = default)
        => Execute(app, tenant, null, request, false, key, null, ct);
    public Task<ManagementPortalCustomerInvitationResult> ResendAsync(Guid app, Guid tenant, Guid invite, string key, string version, CancellationToken ct = default)
        => Execute(app, tenant, invite, null, false, key, version, ct);
    public Task<ManagementPortalCustomerInvitationResult> RevokeAsync(Guid app, Guid tenant, Guid invite, string key, string version, CancellationToken ct = default)
        => Execute(app, tenant, invite, null, true, key, version, ct);
    private Task<ManagementPortalCustomerInvitationResult> Execute(Guid app, Guid tenant, Guid? invite, ProvisionManagementCustomerUser? request, bool revoke,
        string key, string? version, CancellationToken ct)
    {
        var authority = invite is null ? ManagementCustomerIdentityOperations.UsersProvision : ManagementCustomerIdentityOperations.InvitesManage;
        var action = "management.portal.customer-identity." + (invite is null ? "users.provision" : revoke ? "invites.revoke" : "invites.resend");
        return runtime.Run(this, app, action, async token => {
            await runtime.Authorize(app, authority, token);
            if (tenant == Guid.Empty || invite == Guid.Empty || !ManagementDelegationBinding.ValidIdempotencyKey(key) ||
                invite is not null && (string.IsNullOrWhiteSpace(version) || version == "*" || version.Length > 256 || version.Any(char.IsControl)))
                throw new ManagementCustomerIdentityInvalidRequestException();
            if (invite is null) Validate(request);
            var connection = await runtime.Resolve(app, authority, token);
            var state = references.Create(new(authority, tenant, invite ?? tenant), key);
            ManagementIdentityOperationDto? receipt = null; ManagementCustomerInvitationDto? invitation = null; var status = "unknown";
            using (var attempt = runtime.Attempt(token)) {
                try {
                    if (invite is null) {
                        var result = await client.ProvisionUserAsync(connection, tenant, request!, key, attempt.Token);
                        receipt = result.Operation; status = result.Status; invitation = result.Invitation;
                    } else {
                        var result = await client.ChangeInvitationAsync(connection, tenant, invite.Value, revoke, key, version!, attempt.Token);
                        receipt = result.Operation; status = result.Status; invitation = result.Invitation;
                    }
                } catch (Exception ex) when (PortalMutationRuntime.Uncertain(ex, token)) { }
            }
            await runtime.Recheck(connection, authority, token);
            var tracking = references.Result(connection, state, receipt);
            await runtime.Outcome(this, app, action + "." + status, tenant, token);
            return new ManagementPortalCustomerInvitationResult(tracking, status, invitation);
        }, ct);
    }
    private static void Validate(ProvisionManagementCustomerUser? value)
    {
        if (value is null || value.Email is not { Length: >= 3 and <= 320 } || value.Email.Any(char.IsControl) ||
            !MailAddress.TryCreate(value.Email.Trim(), out var email) || email.Address != value.Email.Trim() ||
            value.PolicyKey is not { Length: > 0 and <= 64 } || value.PolicyKey.Any(x => !char.IsAsciiLetterOrDigit(x) && x is not ('-' or '_')) ||
            value.RoleIds is not { Count: > 0 and <= 8 } || value.RoleIds.Any(x => x == Guid.Empty) || value.RoleIds.Distinct().Count() != value.RoleIds.Count)
            throw new ManagementCustomerIdentityInvalidRequestException();
    }
}
