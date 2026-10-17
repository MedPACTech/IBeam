using IBeam.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
namespace IBeam.Management.Api;

[NonController]
[Authorize(Policy = ManagementApiPolicies.Access)]
[ServiceFilter(typeof(ManagementApplicationApiFilter), Order = -3000)]
[RequestSizeLimit(16384)]
[HttpLogging(HttpLoggingFields.None)]
[Route("api/management/applications/{applicationId:guid}/connection")]
public sealed class ManagementApplicationConnectionsController(IManagementApplicationConnectionService service) : ApiControllerBase
{
    [HttpPut]
    public async Task<IActionResult> PutAsync(Guid applicationId, [FromBody] UpdateManagementConnection request, CancellationToken ct)
        => OkResponse(await service.PutAsync(applicationId, request, ct));
    [HttpPost("test")]
    public async Task<IActionResult> TestAsync(Guid applicationId, CancellationToken ct) => OkResponse(await service.TestAsync(applicationId, ct));
}
