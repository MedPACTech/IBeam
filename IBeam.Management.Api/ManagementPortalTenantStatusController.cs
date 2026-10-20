using IBeam.Api.Controllers;
using IBeam.Management.CustomerIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
namespace IBeam.Management.Api;
[NonController, Authorize(Policy = ManagementApiPolicies.Access), HttpLogging(HttpLoggingFields.None)]
[ServiceFilter(typeof(ManagementApplicationApiFilter), Order = -3000)]
[Route("api/management/applications/{applicationId:guid}/tenants/{tenantId:guid}")]
public sealed class ManagementPortalTenantStatusController(IManagementPortalTenantAdministrationService service) : ApiControllerBase
{
    [HttpPost("activate")]
    public async Task<IActionResult> Activate(Guid applicationId, Guid tenantId, [FromBody] ChangeManagementCustomerTenantStatus request,
        [FromHeader(Name = "If-Match")] string version, [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct)
        => Reply(applicationId, await service.SetStatusAsync(applicationId, tenantId, true, version, key, ct));
    [HttpPost("deactivate")]
    public async Task<IActionResult> Deactivate(Guid applicationId, Guid tenantId, [FromBody] ChangeManagementCustomerTenantStatus request,
        [FromHeader(Name = "If-Match")] string version, [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct)
        => Reply(applicationId, await service.SetStatusAsync(applicationId, tenantId, false, version, key, ct));
    private IActionResult Reply(Guid app, ManagementPortalTenantMutationResult result)
    { Response.Headers.Location = Url.Action("GetManagementMutation", new { controller = "ManagementPortalMutationOperations", applicationId = app,
        reference = result.Tracking.Reference, authority = ManagementCustomerIdentityOperations.TenantsStatus });
      var response = (ObjectResult)OkResponse(result); response.StatusCode = result.Tracking.Status == "succeeded" ? 200 : 202; return response; }
}
