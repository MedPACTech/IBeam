using IBeam.Api.Controllers;
using IBeam.Management.CustomerIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
namespace IBeam.Management.Identity.Target.Api;

[NonController, Authorize(Policy = ManagementCustomerIdentityTargetApiExtensions.Policy), HttpLogging(HttpLoggingFields.None)]
[ServiceFilter(typeof(ManagementCustomerIdentityTargetFilter), Order = -3000)]
[Route("api/management-identity/tenants/{tenantId:guid}/invites/{inviteId:guid}")]
public sealed class ManagementCustomerInvitationsController(IManagementCustomerInvitationService service) : ApiControllerBase
{
    [HttpPost("resend"), ManagementCustomerIdentityOperation(ManagementCustomerIdentityOperations.InvitesManage)]
    public async Task<IActionResult> Resend(Guid tenantId, Guid inviteId, [FromBody] ChangeManagementCustomerInvitation request,
        [FromHeader(Name = "Idempotency-Key")] string key, [FromHeader(Name = "If-Match")] string version, CancellationToken ct)
        => Result(await service.ResendAsync(tenantId, inviteId, key, version, ct));
    [HttpPost("revoke"), ManagementCustomerIdentityOperation(ManagementCustomerIdentityOperations.InvitesManage)]
    public async Task<IActionResult> Revoke(Guid tenantId, Guid inviteId, [FromBody] ChangeManagementCustomerInvitation request,
        [FromHeader(Name = "Idempotency-Key")] string key, [FromHeader(Name = "If-Match")] string version, CancellationToken ct)
        => Result(await service.RevokeAsync(tenantId, inviteId, key, version, ct));
    private IActionResult Result(ManagementCustomerInvitationResult value)
    {
        Response.Headers.Location = Url.Action("GetIdentityOperation", new { controller = "ManagementIdentityOperations", operationId = value.Operation.OperationId });
        var response = (ObjectResult)OkResponse(value); response.StatusCode = value.Operation.Status == "succeeded" ? 200 : 202; return response;
    }
}
