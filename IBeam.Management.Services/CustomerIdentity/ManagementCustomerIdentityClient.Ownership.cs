using System.Text.Json;
using IBeam.Management.CustomerIdentity;
namespace IBeam.Management.Services.CustomerIdentity;

public sealed partial class ManagementCustomerIdentityClient
{
    public async Task<ManagementCustomerOwnershipDto> ReadOwnershipAsync(ManagementCustomerIdentityConnection c, Guid tenant, CancellationToken ct = default)
    {
        if (tenant == Guid.Empty) throw new ManagementCustomerIdentityInvalidRequestException();
        await Verify(c, ManagementCustomerIdentityOperations.OwnersRead, ct);
        using var body = await Get(c, ManagementCustomerIdentityOperations.OwnersRead, $"/api/management-identity/tenants/{tenant:D}/ownership", true, ct);
        var value = Data<ManagementCustomerOwnershipDto>(body.RootElement) ?? throw new ManagementCustomerIdentityResponseException();
        if (value.TenantId != tenant || !TransferManagementCustomerOwnership.Exact(value.Version) || value.Owners is null || value.Owners.Count > 5000 ||
            value.Owners.Any(x => x is null || x.UserId == Guid.Empty || !TransferManagementCustomerOwnership.Exact(x.MembershipVersion)) ||
            value.Owners.Select(x => x.UserId).Distinct().Count() != value.Owners.Count) throw new ManagementCustomerIdentityResponseException();
        return new(tenant, value.Version, value.Owners.Select(x => new ManagementCustomerOwnerDto(x.UserId, x.MembershipVersion)).ToArray());
    }
    public async Task<ManagementCustomerOwnershipResult> TransferOwnershipAsync(ManagementCustomerIdentityConnection c, Guid tenant,
        TransferManagementCustomerOwnership request, string version, string key, CancellationToken ct = default)
    {
        if (tenant == Guid.Empty || request is null || !request.IsValid() || !TransferManagementCustomerOwnership.Exact(version))
            throw new ManagementCustomerIdentityInvalidRequestException();
        await Verify(c, ManagementCustomerIdentityOperations.OwnersTransfer, ct, true);
        var response = await Send(c, ManagementCustomerIdentityOperations.OwnersTransfer, "POST", $"/api/management-identity/tenants/{tenant:D}/ownership/transfers", true,
            JsonSerializer.SerializeToUtf8Bytes(request, Json), key, version, false, ct);
        using var document = response.Document;
        var result = Data<ManagementCustomerOwnershipResult>(document.RootElement) ?? throw new ManagementCustomerIdentityResponseException();
        var receipt = SafeOperation(result.Operation, ManagementCustomerIdentityOperations.OwnersTransfer, null);
        if (receipt.ResourceId != tenant || (receipt.Status == "succeeded") != (response.Status == System.Net.HttpStatusCode.OK))
            throw new ManagementCustomerIdentityResponseException();
        return new(receipt);
    }
}
