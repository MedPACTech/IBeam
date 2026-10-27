using IBeam.Api.Controllers;
using IBeam.Management.CustomerIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
namespace IBeam.Management.Api;

[NonController, Authorize(Policy = ManagementApiPolicies.Access), HttpLogging(HttpLoggingFields.None)]
[ServiceFilter(typeof(ManagementApplicationApiFilter), Order = -3000)]
[Route("api/management/applications/{applicationId:guid}/tenants/{tenantId:guid}")]
public sealed class ManagementPortalCustomerProvisioningController(IManagementPortalCustomerProvisioningService service) : ApiControllerBase
{
    [HttpGet("assignable-roles")]
    public async Task<IActionResult> Roles(Guid applicationId, Guid tenantId, CancellationToken ct) => OkResponse(await service.ListRolesAsync(applicationId, tenantId, ct));
    [HttpPost("users/provision")]
    public async Task<IActionResult> Provision(Guid applicationId, Guid tenantId, [FromBody] ProvisionManagementCustomerUser request,
        [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct)
        => Reply(applicationId, ManagementCustomerIdentityOperations.UsersProvision, await service.ProvisionAsync(applicationId, tenantId, request, key, ct));
    [HttpPost("invites/{inviteId:guid}/resend")]
    public async Task<IActionResult> Resend(Guid applicationId, Guid tenantId, Guid inviteId, [FromBody] ChangeManagementCustomerInvitation request,
        [FromHeader(Name = "Idempotency-Key")] string key, [FromHeader(Name = "If-Match")] string version, CancellationToken ct)
        => Reply(applicationId, ManagementCustomerIdentityOperations.InvitesManage, await service.ResendAsync(applicationId, tenantId, inviteId, key, version, ct));
    [HttpPost("invites/{inviteId:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid applicationId, Guid tenantId, Guid inviteId, [FromBody] ChangeManagementCustomerInvitation request,
        [FromHeader(Name = "Idempotency-Key")] string key, [FromHeader(Name = "If-Match")] string version, CancellationToken ct)
        => Reply(applicationId, ManagementCustomerIdentityOperations.InvitesManage, await service.RevokeAsync(applicationId, tenantId, inviteId, key, version, ct));
    private IActionResult Reply(Guid app, string authority, ManagementPortalCustomerInvitationResult result)
    {
        Response.Headers.Location = Url.Action("GetManagementMutation", new { controller = "ManagementPortalMutationOperations", applicationId = app, reference = result.Tracking.Reference, authority });
        var response = (ObjectResult)OkResponse(result); response.StatusCode = result.Status == "accepted" || authority == ManagementCustomerIdentityOperations.InvitesManage &&
            result.Tracking.Operation?.Status == "succeeded" ? 200 : 202; return response;
    }
}
