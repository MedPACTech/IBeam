using IBeam.Api.Controllers;
using IBeam.Management.CustomerIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
namespace IBeam.Management.Identity.Target.Api;

[NonController, Authorize(Policy = ManagementCustomerIdentityTargetApiExtensions.Policy), HttpLogging(HttpLoggingFields.None)]
[ServiceFilter(typeof(ManagementCustomerIdentityTargetFilter), Order = -3000)]
[Route("api/management-identity/tenants")]
public sealed class ManagementTenantAdministrationController(IManagementTenantAdministrationService service) : ApiControllerBase
{
    [HttpPost, ManagementCustomerIdentityOperation(ManagementCustomerIdentityOperations.TenantsCreate)]
    public async Task<IActionResult> CreateAsync([FromBody] CreateManagementCustomerTenant request, [FromHeader(Name = "Idempotency-Key")] string idempotencyKey, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, idempotencyKey, ct);
        if (result.Operation.Status == "succeeded") return CreatedResponse("GetIdentityOperation", new { controller = "ManagementIdentityOperations", operationId = result.Operation.OperationId }, result);
        var response = (ObjectResult)OkResponse(result); response.StatusCode = 202; return response;
    }
    [HttpPatch("{tenantId:guid}"), ManagementCustomerIdentityOperation(ManagementCustomerIdentityOperations.TenantsUpdate)]
    public async Task<IActionResult> RenameAsync(Guid tenantId, [FromBody] RenameManagementCustomerTenant request,
        [FromHeader(Name = "If-Match")] string expectedVersion, [FromHeader(Name = "Idempotency-Key")] string idempotencyKey, CancellationToken ct)
    {
        var result = await service.RenameAsync(tenantId, request, expectedVersion, idempotencyKey, ct);
        var response = (ObjectResult)OkResponse(result); response.StatusCode = result.Operation.Status == "succeeded" ? 200 : 202; return response;
    }
}
