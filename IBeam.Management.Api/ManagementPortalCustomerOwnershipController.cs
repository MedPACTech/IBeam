using IBeam.Api.Controllers;
using IBeam.Management.CustomerIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
namespace IBeam.Management.Api;

[NonController, Authorize(Policy = ManagementApiPolicies.Access), HttpLogging(HttpLoggingFields.None)]
[ServiceFilter(typeof(ManagementApplicationApiFilter), Order = -3000)]
[Route("api/management/applications/{applicationId:guid}/tenants/{tenantId:guid}/ownership")]
public sealed class ManagementPortalCustomerOwnershipController(IManagementPortalCustomerOwnershipService service) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Read(Guid applicationId, Guid tenantId, CancellationToken ct) => OkResponse(await service.ReadAsync(applicationId, tenantId, ct));
    [HttpPost("transfers")]
    public async Task<IActionResult> Transfer(Guid applicationId, Guid tenantId, [FromBody] TransferManagementCustomerOwnership request,
        [FromHeader(Name = "If-Match")] string version, [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct)
    {
        var result = await service.TransferAsync(applicationId, tenantId, request, version, key, ct);
        Response.Headers.Location = Url.Action("GetManagementMutation", new { controller = "ManagementPortalMutationOperations", applicationId,
            reference = result.Tracking.Reference, authority = ManagementCustomerIdentityOperations.OwnersTransfer });
        var response = (ObjectResult)OkResponse(result); response.StatusCode = result.Tracking.Status == "succeeded" ? 200 : 202; return response;
    }
}
