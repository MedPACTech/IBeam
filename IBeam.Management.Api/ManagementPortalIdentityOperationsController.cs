using IBeam.Api.Controllers;
using IBeam.Management.CustomerIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
namespace IBeam.Management.Api;

[NonController]
[Authorize(Policy = ManagementApiPolicies.Access)]
[ServiceFilter(typeof(ManagementApplicationApiFilter), Order = -3000)]
[HttpLogging(HttpLoggingFields.None)]
[Route("api/management/applications/{applicationId:guid}/operations")]
public sealed class ManagementPortalIdentityOperationsController(IManagementPortalIdentityOperationService service) : ApiControllerBase
{
    [HttpGet("{operationId:guid}")]
    public async Task<IActionResult> GetAsync(Guid applicationId, Guid operationId, [FromQuery] string authority, CancellationToken ct)
        => OkResponse(await service.GetAsync(applicationId, operationId, authority, ct));
}
