using IBeam.Api.Controllers;
using IBeam.Credits.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.Credits.Api;

[Authorize]
[Route("api/credits/tenants/{tenantId:guid}")]
public sealed class CreditRuntimeController : ApiControllerBase
{
    private readonly ICreditBalanceSummaryService _summaries;

    public CreditRuntimeController(ICreditBalanceSummaryService summaries)
    {
        _summaries = summaries;
    }

    [HttpPost("runtime-summary")]
    public async Task<IActionResult> GetRuntimeSummaryAsync(
        Guid tenantId,
        GetCreditRuntimeSummaryRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _summaries.GetRuntimeSummaryAsync(tenantId, request, ct).ConfigureAwait(false);
            return OkResponse(result);
        }
        catch (CreditException ex)
        {
            return BadRequestResponse(ex.Message);
        }
    }
}
