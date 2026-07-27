using IBeam.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.Licensing.Api;

[Authorize]
[Route("api/licensing")]
public sealed class LicensingRuntimeController : ApiControllerBase
{
    private readonly ILicenseRuntimeContextService _runtimeContext;

    public LicensingRuntimeController(ILicenseRuntimeContextService runtimeContext)
    {
        _runtimeContext = runtimeContext;
    }

    [HttpPost("tenants/{tenantId:guid}/runtime-context")]
    public async Task<IActionResult> GetRuntimeContextAsync(
        Guid tenantId,
        GetLicenseRuntimeContextRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _runtimeContext.GetRuntimeContextAsync(tenantId, request, ct).ConfigureAwait(false);
            return OkResponse(result);
        }
        catch (LicensingException ex)
        {
            return BadRequestResponse(ex.Message);
        }
    }
}
