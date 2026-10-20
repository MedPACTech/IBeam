using System.Text.Json;
using IBeam.Management.CustomerIdentity;
namespace IBeam.Management.Services.CustomerIdentity;
public sealed partial class ManagementCustomerIdentityClient
{
    public async Task<ManagementTenantMutationResult> SetTenantStatusAsync(ManagementCustomerIdentityConnection c, Guid tenant, bool active, string version, string key, CancellationToken ct = default)
    {
        if (tenant == Guid.Empty) throw new ManagementCustomerIdentityInvalidRequestException();
        const string authority = ManagementCustomerIdentityOperations.TenantsStatus;
        await Verify(c, authority, ct, true);
        var response = await Send(c, authority, "POST", $"/api/management-identity/tenants/{tenant:D}/" + (active ? "activate" : "deactivate"), true,
            JsonSerializer.SerializeToUtf8Bytes(new ChangeManagementCustomerTenantStatus(), Json), key, version, false, ct);
        using var document = response.Document;
        var result = Data<ManagementTenantMutationResult>(document.RootElement) ?? throw new ManagementCustomerIdentityResponseException();
        var receipt = SafeOperation(result.Operation, authority, null);
        if (receipt.ResourceId != tenant || !ManagementTenantSources.IsValid(result.Source) ||
            result.ExternalReference is { } reference && (reference.Length is < 1 or > 128 || reference.Any(x => !char.IsAsciiLetterOrDigit(x) && x is not ('-' or '_' or '.' or ':' or '/'))) ||
            receipt.Status == "succeeded" && result.Tenant is null) throw new ManagementCustomerIdentityResponseException();
        return new(receipt, result.Tenant is null ? null : Safe(result.Tenant, null, tenant), result.Source, result.ExternalReference);
    }
}
