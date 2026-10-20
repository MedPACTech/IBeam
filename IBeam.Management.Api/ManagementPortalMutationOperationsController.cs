using IBeam.Api.Controllers;
using IBeam.Management.CustomerIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
namespace IBeam.Management.Api;

[NonController, Authorize(Policy = ManagementApiPolicies.Access), HttpLogging(HttpLoggingFields.None)]
[ServiceFilter(typeof(ManagementApplicationApiFilter), Order = -3000)]
[Route("api/management/applications/{applicationId:guid}/operations")]
public sealed class ManagementPortalMutationOperationsController(IManagementPortalMutationOperationService service) : ApiControllerBase
{
    [HttpGet("{reference}"), ActionName("GetManagementMutation")]
    public async Task<IActionResult> GetAsync(Guid applicationId, string reference, [FromQuery] string authority, CancellationToken ct)
    {
        var result = await service.GetAsync(applicationId, reference, authority, ct);
        var response = (ObjectResult)OkResponse(result); response.StatusCode = result.Status == "succeeded" || result.Operation?.Onboarding?.Completed == true ||
            result.Operation?.Invitation?.Status is "accepted" or "expired" or "revoked" ||
            result.Operation?.Operation == ManagementCustomerIdentityOperations.InvitesManage && result.Operation.Status == "succeeded" ? 200 : 202; return response;
    }
}
