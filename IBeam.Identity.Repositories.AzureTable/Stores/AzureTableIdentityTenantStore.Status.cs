using Azure;
using Azure.Data.Tables;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Exceptions;
using IBeam.Identity.Repositories.AzureTable.Entities;
namespace IBeam.Identity.Repositories.AzureTable.Stores;
public sealed partial class AzureTableIdentityTenantStore : IIdentityTenantStatusConditionalStore
{
    public async Task<VersionedIdentityTenant> SetStatusConditionalAsync(Guid tenant, bool active, string version, Guid operation, CancellationToken ct = default)
    {
        if (tenant == Guid.Empty || string.IsNullOrWhiteSpace(version) || version == "*" || version.Length > 256 || operation == Guid.Empty)
            throw new IdentityValidationException("An exact tenant version and operation are required.");
        var current = await FindVersionedAsync(tenant, ct) ?? throw new IdentityNotFoundException("Tenant was not found.");
        var status = active ? IdentityTenantStatuses.Active : IdentityTenantStatuses.Disabled;
        if (current.LastOperationId == operation && current.Tenant.Status == status) return current;
        if (current.Version != version) throw new IdentityConcurrencyException();
        var now = DateTimeOffset.UtcNow; var patch = new TableEntity(TenantEntity.TenantsPartitionKey, tenant.ToString("D")) {
            ["Status"] = status, ["UpdatedAt"] = now, ["LastOperationId"] = operation };
        try { var response = await IdentityTenantPurgePartition.Update(TenantsTable(), patch, new ETag(version), TableUpdateMode.Merge, _opts.RequireTenantPurgeFencing, ct);
            return new(current.Tenant with { Status = status, UpdatedAt = now }, response.Headers.ETag?.ToString() ?? throw new IdentityProviderException("Tenant version unavailable."), operation); }
        catch (RequestFailedException ex) when (ex.Status is 404 or 412) { throw new IdentityConcurrencyException(); }
    }
}
