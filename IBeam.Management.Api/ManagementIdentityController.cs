using IBeam.Api.Controllers;
using IBeam.Management;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.Management.Api;

[Authorize(Policy = ManagementApiPolicies.Access)]
[ServiceFilter(typeof(ManagementAccessDeniedFilter))]
[Route("api/management")]
public sealed class ManagementIdentityController(IManagementIdentityService service) : ApiControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentAsync(CancellationToken ct)
        => OkResponse(await service.GetCurrentAsync(ct).ConfigureAwait(false));
}
