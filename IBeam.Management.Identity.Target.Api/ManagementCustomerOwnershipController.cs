using IBeam.Api.Controllers;
using IBeam.Management.CustomerIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
namespace IBeam.Management.Identity.Target.Api;

[NonController, Authorize(Policy = ManagementCustomerIdentityTargetApiExtensions.Policy), HttpLogging(HttpLoggingFields.None)]
[ServiceFilter(typeof(ManagementCustomerIdentityTargetFilter), Order = -3000)]
[Route("api/management-identity/tenants/{tenantId:guid}/ownership")]
public sealed class ManagementCustomerOwnershipController(IManagementCustomerOwnershipService service) : ApiControllerBase
{
    [HttpGet, ManagementCustomerIdentityOperation(ManagementCustomerIdentityOperations.OwnersRead)]
    public async Task<IActionResult> Read(Guid tenantId, CancellationToken ct) => OkResponse(await service.ReadAsync(tenantId, ct));
    [HttpPost("transfers"), ManagementCustomerIdentityOperation(ManagementCustomerIdentityOperations.OwnersTransfer)]
    public async Task<IActionResult> Transfer(Guid tenantId, [FromBody] TransferManagementCustomerOwnership request,
        [FromHeader(Name = "If-Match")] string version, [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct)
    {
        var result = await service.TransferAsync(tenantId, request, version, key, ct);
        Response.Headers.Location = Url.Action("GetIdentityOperation", new { controller = "ManagementIdentityOperations", operationId = result.Operation.OperationId });
        var response = (ObjectResult)OkResponse(result); response.StatusCode = result.Operation.Status == "succeeded" ? 200 : 202; return response;
    }
}
