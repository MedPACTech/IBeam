using IBeam.Api.Controllers;
using IBeam.Management.CustomerIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
namespace IBeam.Management.Api;

[NonController]
[Authorize(Policy = ManagementApiPolicies.Access)]
[ServiceFilter(typeof(ManagementApplicationApiFilter))]
[HttpLogging(HttpLoggingFields.None)]
[Route("api/management/applications/{applicationId:guid}/customer-management")]
public sealed class ManagementCustomerIdentityController(IManagementCustomerIdentityAvailabilityService service) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAsync(Guid applicationId, CancellationToken ct) => OkResponse(await service.GetAsync(applicationId, ct));
}
