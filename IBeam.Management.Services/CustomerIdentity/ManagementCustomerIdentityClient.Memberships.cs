using System.Text.Json;
using IBeam.Management.CustomerIdentity;
namespace IBeam.Management.Services.CustomerIdentity;
public sealed partial class ManagementCustomerIdentityClient
{
    public Task<ManagementCustomerMembershipResult> SetMembershipAsync(ManagementCustomerIdentityConnection c, Guid tenant, Guid user,
        ChangeManagementCustomerMembership request, string version, string key, CancellationToken ct = default)
        => Membership(c, tenant, user, request, "PATCH", "membership", ManagementCustomerIdentityOperations.MembershipsManage, version, key, ct);
    public Task<ManagementCustomerMembershipResult> SetCustomerRolesAsync(ManagementCustomerIdentityConnection c, Guid tenant, Guid user,
        ChangeManagementCustomerRoles request, string version, string key, CancellationToken ct = default)
        => Membership(c, tenant, user, request, "PUT", "roles", ManagementCustomerIdentityOperations.RolesAssign, version, key, ct);
    private async Task<ManagementCustomerMembershipResult> Membership<T>(ManagementCustomerIdentityConnection c, Guid tenant, Guid user, T payload,
        string method, string suffix, string authority, string version, string key, CancellationToken ct)
    {
        if (tenant == Guid.Empty || user == Guid.Empty) throw new ManagementCustomerIdentityInvalidRequestException();
        await Verify(c, authority, ct, true);
        var response = await Send(c, authority, method, $"/api/management-identity/tenants/{tenant:D}/users/{user:D}/{suffix}", true,
            JsonSerializer.SerializeToUtf8Bytes(payload, Json), key, version, false, ct);
        using var document = response.Document;
        var result = Data<ManagementCustomerMembershipResult>(document.RootElement) ?? throw new ManagementCustomerIdentityResponseException();
        var receipt = SafeOperation(result.Operation, authority, null);
        if (receipt.ResourceId != user || receipt.Status == "succeeded" && result.Membership is null || result.Membership is { } member && !Text(member.Version, 256)) throw new ManagementCustomerIdentityResponseException();
        return new(receipt, result.Membership is null ? null : Safe(result.Membership, tenant, user));
    }
}
