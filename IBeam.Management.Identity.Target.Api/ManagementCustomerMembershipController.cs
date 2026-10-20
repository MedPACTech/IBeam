using IBeam.Api.Controllers;
using IBeam.Management.CustomerIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
namespace IBeam.Management.Identity.Target.Api;
[NonController, Authorize(Policy = ManagementCustomerIdentityTargetApiExtensions.Policy), HttpLogging(HttpLoggingFields.None)]
[ServiceFilter(typeof(ManagementCustomerIdentityTargetFilter), Order = -3000)]
[Route("api/management-identity/tenants/{tenantId:guid}/users/{userId:guid}")]
public sealed class ManagementCustomerMembershipController(IManagementCustomerMembershipService service) : ApiControllerBase
{
    [HttpPatch("membership"), ManagementCustomerIdentityOperation(ManagementCustomerIdentityOperations.MembershipsManage)]
    public async Task<IActionResult> Status(Guid tenantId, Guid userId, [FromBody] ChangeManagementCustomerMembership request,
        [FromHeader(Name = "If-Match")] string version, [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct)
        => Reply(await service.SetEnabledAsync(tenantId, userId, request, version, key, ct));
    [HttpPut("roles"), ManagementCustomerIdentityOperation(ManagementCustomerIdentityOperations.RolesAssign)]
    public async Task<IActionResult> Roles(Guid tenantId, Guid userId, [FromBody] ChangeManagementCustomerRoles request,
        [FromHeader(Name = "If-Match")] string version, [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct)
        => Reply(await service.SetRolesAsync(tenantId, userId, request, version, key, ct));
    private IActionResult Reply(ManagementCustomerMembershipResult result)
    { Response.Headers.Location = Url.Action("GetIdentityOperation", new { controller = "ManagementIdentityOperations", operationId = result.Operation.OperationId });
      var response = (ObjectResult)OkResponse(result); response.StatusCode = result.Operation.Status == "succeeded" ? 200 : 202; return response; }
}
