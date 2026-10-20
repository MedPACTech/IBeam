using IBeam.Api.Controllers;
using IBeam.Management.CustomerIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
namespace IBeam.Management.Identity.Target.Api;

[NonController, Authorize(Policy = ManagementCustomerIdentityTargetApiExtensions.Policy), HttpLogging(HttpLoggingFields.None)]
[ServiceFilter(typeof(ManagementCustomerIdentityTargetFilter), Order = -3000)]
[Route("api/management-identity/tenants/{tenantId:guid}")]
public sealed class ManagementCustomerProvisioningController(IManagementCustomerProvisioningService service) : ApiControllerBase
{
    [HttpPost("users/provision"), ManagementCustomerIdentityOperation(ManagementCustomerIdentityOperations.UsersProvision)]
    public async Task<IActionResult> Provision(Guid tenantId, [FromBody] ProvisionManagementCustomerUser request, [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct)
    {
        var result = await service.ProvisionAsync(tenantId, request, key, ct);
        Response.Headers.Location = Url.Action("GetIdentityOperation", new { controller = "ManagementIdentityOperations", operationId = result.Operation.OperationId });
        var response = (ObjectResult)OkResponse(result); response.StatusCode = result.Status == "accepted" ? 200 : 202; return response;
    }
}
[NonController, Authorize(Policy = ManagementCustomerIdentityTargetApiExtensions.Policy), HttpLogging(HttpLoggingFields.None)]
[ServiceFilter(typeof(ManagementCustomerIdentityTargetFilter), Order = -3000)]
[Route("api/management-identity/tenants/{tenantId:guid}/assignable-roles")]
public sealed class ManagementAssignableRolesController(IManagementAssignableRoleCatalogService service) : ApiControllerBase
{
    [HttpGet, ManagementCustomerIdentityOperation(ManagementCustomerIdentityOperations.RolesRead)]
    public async Task<IActionResult> Get(Guid tenantId, CancellationToken ct) => OkResponse(await service.ListAsync(tenantId, ct));
}
