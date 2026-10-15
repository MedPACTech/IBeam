using IBeam.Api.Controllers;
using IBeam.Management;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.Management.Diagnostics.Api;

[Authorize(Policy = ManagementDiagnosticsApiPolicies.Access)]
[ServiceFilter(typeof(ManagementAccessDeniedFilter))]
[Route("api/management-diagnostics")]
public sealed class ManagementDiagnosticsInfoController(IManagementDiagnosticsInfoService service) : ApiControllerBase
{
    [HttpGet("info")]
    public async Task<IActionResult> GetInfoAsync(CancellationToken ct)
        => OkResponse(await service.GetAsync(ct).ConfigureAwait(false));
}
