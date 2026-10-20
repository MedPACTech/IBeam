using IBeam.Api.Controllers;
using IBeam.Management.CustomerIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
namespace IBeam.Management.Api;

[NonController, Authorize(Policy = ManagementApiPolicies.Access), HttpLogging(HttpLoggingFields.None)]
[ServiceFilter(typeof(ManagementApplicationApiFilter), Order = -3000)]
[Route("api/management/applications/{applicationId:guid}/tenants")]
public sealed class ManagementPortalTenantAdministrationController(IManagementPortalTenantAdministrationService service) : ApiControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateAsync(Guid applicationId, [FromBody] CreateManagementCustomerTenant request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey, CancellationToken ct)
    {
        var result = await service.CreateAsync(applicationId, request, idempotencyKey, ct);
        var route = new { controller = "ManagementPortalMutationOperations", applicationId, reference = result.Tracking.Reference,
            authority = ManagementCustomerIdentityOperations.TenantsCreate };
        if (result.Tracking.Status == "succeeded") return CreatedResponse("GetManagementMutation", route, result);
        Response.Headers.Location = Url.Action("GetManagementMutation", route);
        var response = (ObjectResult)OkResponse(result); response.StatusCode = 202; return response;
    }
    [HttpPatch("{tenantId:guid}")]
    public async Task<IActionResult> RenameAsync(Guid applicationId, Guid tenantId, [FromBody] RenameManagementCustomerTenant request,
        [FromHeader(Name = "If-Match")] string expectedVersion, [FromHeader(Name = "Idempotency-Key")] string idempotencyKey, CancellationToken ct)
    {
        var result = await service.RenameAsync(applicationId, tenantId, request, expectedVersion, idempotencyKey, ct);
        var response = (ObjectResult)OkResponse(result); response.StatusCode = result.Tracking.Status == "succeeded" ? 200 : 202; return response;
    }
}
