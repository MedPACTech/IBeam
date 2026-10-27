using IBeam.Api.Controllers;
using IBeam.Management.CustomerIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
namespace IBeam.Management.Identity.Target.Api;
[NonController, Authorize(Policy = ManagementCustomerIdentityTargetApiExtensions.Policy), HttpLogging(HttpLoggingFields.None)]
[ServiceFilter(typeof(ManagementCustomerIdentityTargetFilter), Order = -3000)]
[Route("api/management-identity/tenants/{tenantId:guid}")]
public sealed class ManagementTenantStatusController(IManagementTenantStatusService service) : ApiControllerBase
{
    [HttpPost("activate"), ManagementCustomerIdentityOperation(ManagementCustomerIdentityOperations.TenantsStatus)]
    public async Task<IActionResult> Activate(Guid tenantId, [FromBody] ChangeManagementCustomerTenantStatus request,
        [FromHeader(Name = "If-Match")] string version, [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct)
        => Reply(await service.SetStatusAsync(tenantId, true, version, key, ct));
    [HttpPost("deactivate"), ManagementCustomerIdentityOperation(ManagementCustomerIdentityOperations.TenantsStatus)]
    public async Task<IActionResult> Deactivate(Guid tenantId, [FromBody] ChangeManagementCustomerTenantStatus request,
        [FromHeader(Name = "If-Match")] string version, [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct)
        => Reply(await service.SetStatusAsync(tenantId, false, version, key, ct));
    private IActionResult Reply(ManagementTenantMutationResult result)
    { Response.Headers.Location = Url.Action("GetIdentityOperation", new { controller = "ManagementIdentityOperations", operationId = result.Operation.OperationId });
      var response = (ObjectResult)OkResponse(result); response.StatusCode = result.Operation.Status == "succeeded" ? 200 : 202; return response; }
}
