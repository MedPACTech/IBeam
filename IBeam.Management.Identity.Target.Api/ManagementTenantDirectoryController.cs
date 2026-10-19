using IBeam.Api.Controllers;
using IBeam.Management.CustomerIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
namespace IBeam.Management.Identity.Target.Api;

[NonController, Authorize(Policy = ManagementCustomerIdentityTargetApiExtensions.Policy), HttpLogging(HttpLoggingFields.None)]
[ServiceFilter(typeof(ManagementCustomerIdentityTargetFilter), Order = -3000)]
[Route("api/management-identity/tenants")]
public sealed class ManagementTenantDirectoryController(IManagementTenantDirectoryService service) : ApiControllerBase
{
    [HttpGet, ManagementCustomerIdentityOperation(ManagementCustomerIdentityOperations.TenantsRead)]
    public async Task<IActionResult> List([FromQuery] ManagementTenantDirectoryRequest query, CancellationToken ct)
    {
        var page = await service.ListAsync(new(query.PageSize, query.ContinuationToken, query.Status, query.NamePrefix), ct);
        return OkCursorPagedResponse(page.Items, page.PageSize, page.ContinuationToken);
    }
    [HttpGet("{tenantId:guid}"), ManagementCustomerIdentityOperation(ManagementCustomerIdentityOperations.TenantsRead)]
    public async Task<IActionResult> Find(Guid tenantId, CancellationToken ct) => OkResponse(await service.FindAsync(tenantId, ct));
}
public sealed class ManagementTenantDirectoryRequest
{
    public int PageSize { get; set; } = 50;
    public string? ContinuationToken { get; set; }
    public string? Status { get; set; }
    public string? NamePrefix { get; set; }
    public override string ToString() => "Customer tenant directory query (state redacted)";
}
