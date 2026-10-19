using IBeam.Api.Controllers;
using IBeam.Management.CustomerIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
namespace IBeam.Management.Identity.Target.Api;

[NonController, Authorize(Policy = ManagementCustomerIdentityTargetApiExtensions.Policy), HttpLogging(HttpLoggingFields.None)]
[ServiceFilter(typeof(ManagementCustomerIdentityTargetFilter), Order = -3000)]
[Route("api/management-identity/tenants/{tenantId:guid}/users")]
public sealed class ManagementCustomerUserDirectoryController(IManagementCustomerUserDirectoryService service) : ApiControllerBase
{
    [HttpGet, ManagementCustomerIdentityOperation(ManagementCustomerIdentityOperations.UsersRead)]
    public async Task<IActionResult> List(Guid tenantId, [FromQuery] ManagementTenantDirectoryRequest query, CancellationToken ct)
    {
        var page = await service.ListAsync(tenantId, new(query.PageSize, query.ContinuationToken, query.Status, query.NamePrefix), ct);
        return OkCursorPagedResponse(page.Items, page.PageSize, page.ContinuationToken);
    }
    [HttpGet("{userId:guid}"), ManagementCustomerIdentityOperation(ManagementCustomerIdentityOperations.UsersRead)]
    public async Task<IActionResult> Find(Guid tenantId, Guid userId, CancellationToken ct) => OkResponse(await service.FindAsync(tenantId, userId, ct));
}
