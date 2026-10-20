using System.Text.Json;
using IBeam.Management.CustomerIdentity;
namespace IBeam.Management.Services.CustomerIdentity;

public sealed partial class ManagementCustomerIdentityClient
{
    public async Task<IReadOnlyList<ManagementAssignableRoleDto>> ListAssignableRolesAsync(ManagementCustomerIdentityConnection c, Guid tenantId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty) throw new ManagementCustomerIdentityInvalidRequestException();
        await Verify(c, ManagementCustomerIdentityOperations.RolesRead, ct);
        using var body = await Get(c, ManagementCustomerIdentityOperations.RolesRead, $"/api/management-identity/tenants/{tenantId:D}/assignable-roles", true, ct);
        var rows = Data<ManagementAssignableRoleDto[]>(body.RootElement) ?? throw new ManagementCustomerIdentityResponseException();
        if (rows.Length > 32 || rows.Select(x => x?.RoleId).Distinct().Count() != rows.Length) throw new ManagementCustomerIdentityResponseException();
        return rows.Select(row => row is null || row.TenantId != tenantId || row.RoleId == Guid.Empty || !Text(row.Name, 100) ||
            row.PolicyKeys is not { Count: > 0 and <= 16 } || row.PolicyKeys.Any(x => !PolicyKey(x))
            ? throw new ManagementCustomerIdentityResponseException() : new ManagementAssignableRoleDto(tenantId, row.RoleId, row.Name, row.PolicyKeys.Distinct(StringComparer.Ordinal).ToArray())).ToArray();
    }
    public async Task<ManagementCustomerProvisioningResult> ProvisionUserAsync(ManagementCustomerIdentityConnection c, Guid tenantId,
        ProvisionManagementCustomerUser request, string key, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty) throw new ManagementCustomerIdentityInvalidRequestException();
        const string operation = ManagementCustomerIdentityOperations.UsersProvision;
        await Verify(c, operation, ct, true);
        var response = await Send(c, operation, "POST", $"/api/management-identity/tenants/{tenantId:D}/users/provision", true,
            JsonSerializer.SerializeToUtf8Bytes(request, Json), key, null, false, ct);
        using var document = response.Document;
        var result = Data<ManagementCustomerProvisioningResult>(document.RootElement) ?? throw new ManagementCustomerIdentityResponseException();
        var receipt = SafeOperation(result.Operation, operation, null);
        if (receipt.ResourceId != tenantId || !InvitationOutcome(result.Status) || result.Invitation is not null && result.Invitation.TenantId != tenantId ||
            receipt.Invitation is { } details && details.TenantId != tenantId)
            throw new ManagementCustomerIdentityResponseException();
        if (result.Invitation is { } invite && (!string.Equals(invite.Email, request.Email.Trim(), StringComparison.OrdinalIgnoreCase) ||
            invite.RoleIds is null || !invite.RoleIds.Order().SequenceEqual(request.RoleIds.Order()))) throw new ManagementCustomerIdentityResponseException();
        Consistent(receipt.Invitation, result.Invitation, result.Status);
        return new(receipt, result.Status, result.Invitation is null ? null : Safe(result.Invitation, tenantId, null));
    }
    public async Task<ManagementCustomerInvitationResult> ChangeInvitationAsync(ManagementCustomerIdentityConnection c, Guid tenantId, Guid inviteId,
        bool revoke, string key, string version, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty || inviteId == Guid.Empty) throw new ManagementCustomerIdentityInvalidRequestException();
        const string operation = ManagementCustomerIdentityOperations.InvitesManage;
        await Verify(c, operation, ct, true);
        var response = await Send(c, operation, "POST", $"/api/management-identity/tenants/{tenantId:D}/invites/{inviteId:D}/" + (revoke ? "revoke" : "resend"), true,
            JsonSerializer.SerializeToUtf8Bytes(new ChangeManagementCustomerInvitation(), Json), key, version, false, ct);
        using var document = response.Document;
        var result = Data<ManagementCustomerInvitationResult>(document.RootElement) ?? throw new ManagementCustomerIdentityResponseException();
        var receipt = SafeOperation(result.Operation, operation, null);
        if (receipt.ResourceId != inviteId || !InvitationOutcome(result.Status) || receipt.Invitation is { } details && details.TenantId != tenantId) throw new ManagementCustomerIdentityResponseException();
        Consistent(receipt.Invitation, result.Invitation, result.Status);
        return new(receipt, result.Status == "redeemed" ? "accepted" : result.Status, result.Invitation is null ? null : Safe(result.Invitation, tenantId, inviteId));
    }
    private static bool PolicyKey(string? value) => value is { Length: > 0 and <= 64 } && value.All(x => char.IsAsciiLetterOrDigit(x) || x is '-' or '_');
    private static bool InvitationOutcome(string? status) => status is "pending" or "sending" or "sent" or "failed" or "unknown" or "accepted" or "redeemed" or "expired" or "revoked" or "superseded" or "not-required";
    private static void Consistent(ManagementCustomerInvitationOperationDto? projection, ManagementCustomerInvitationDto? invitation, string outcome)
    {
        if (invitation is not null && projection is not null && (projection.TenantId != invitation.TenantId || projection.InviteId != invitation.InviteId ||
            projection.Status != (invitation.Status == "redeemed" ? "accepted" : invitation.Status) || projection.Version != invitation.Version)) throw new ManagementCustomerIdentityResponseException();
        var terminal = invitation?.Status is "redeemed" or "expired" or "revoked";
        if (terminal && (outcome == "redeemed" ? "accepted" : outcome) != (invitation!.Status == "redeemed" ? "accepted" : invitation.Status))
            throw new ManagementCustomerIdentityResponseException();
    }
    private static ManagementCustomerInvitationOperationDto SafeInvitationOperation(ManagementCustomerInvitationOperationDto value, ManagementIdentityOperationDto receipt)
    {
        if (value.TenantId == Guid.Empty || value.InviteId == Guid.Empty || value.Status is not ("pending" or "sent" or "accepted" or "expired" or "revoked") ||
            value.DeliveryStatus is not ("pending" or "sending" or "sent" or "failed" or "unknown" or "superseded" or "not-required") || !Text(value.Version, 256) ||
            receipt.Operation is not (ManagementCustomerIdentityOperations.UsersProvision or ManagementCustomerIdentityOperations.InvitesManage) ||
            (receipt.Operation == ManagementCustomerIdentityOperations.UsersProvision ? value.TenantId : value.InviteId) != receipt.ResourceId)
            throw new ManagementCustomerIdentityResponseException();
        return new(value.TenantId, value.InviteId, value.Status, value.DeliveryStatus, value.Version);
    }
}
