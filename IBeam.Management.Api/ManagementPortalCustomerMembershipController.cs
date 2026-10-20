using IBeam.Api.Controllers;
using IBeam.Management.CustomerIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
namespace IBeam.Management.Api;
[NonController, Authorize(Policy = ManagementApiPolicies.Access), HttpLogging(HttpLoggingFields.None)]
[ServiceFilter(typeof(ManagementApplicationApiFilter), Order = -3000)]
[Route("api/management/applications/{applicationId:guid}/tenants/{tenantId:guid}/users/{userId:guid}")]
public sealed class ManagementPortalCustomerMembershipController(IManagementPortalCustomerMembershipService service) : ApiControllerBase
{
    [HttpPatch("membership")]
    public async Task<IActionResult> Status(Guid applicationId, Guid tenantId, Guid userId, [FromBody] ChangeManagementCustomerMembership request,
        [FromHeader(Name = "If-Match")] string version, [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct)
        => Reply(applicationId, ManagementCustomerIdentityOperations.MembershipsManage, await service.SetEnabledAsync(applicationId, tenantId, userId, request, version, key, ct));
    [HttpPut("roles")]
    public async Task<IActionResult> Roles(Guid applicationId, Guid tenantId, Guid userId, [FromBody] ChangeManagementCustomerRoles request,
        [FromHeader(Name = "If-Match")] string version, [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct)
        => Reply(applicationId, ManagementCustomerIdentityOperations.RolesAssign, await service.SetRolesAsync(applicationId, tenantId, userId, request, version, key, ct));
    private IActionResult Reply(Guid app, string authority, ManagementPortalCustomerMembershipResult result)
    { Response.Headers.Location = Url.Action("GetManagementMutation", new { controller = "ManagementPortalMutationOperations", applicationId = app, reference = result.Tracking.Reference, authority });
      var response = (ObjectResult)OkResponse(result); response.StatusCode = result.Tracking.Status == "succeeded" ? 200 : 202; return response; }
}
