using IBeam.Api.Controllers;
using IBeam.Credits.Services;
using IBeam.Licensing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.Credits.Api;

[Authorize]
[Route("api/credits/tenants/{tenantId:guid}")]
public sealed class CreditBootstrapController : ApiControllerBase
{
    private readonly ILicenseRuntimeContextService _licenseRuntime;
    private readonly ICreditBalanceSummaryService _creditSummaries;

    public CreditBootstrapController(
        ILicenseRuntimeContextService licenseRuntime,
        ICreditBalanceSummaryService creditSummaries)
    {
        _licenseRuntime = licenseRuntime;
        _creditSummaries = creditSummaries;
    }

    [HttpPost("bootstrap")]
    public async Task<IActionResult> GetBootstrapAsync(
        Guid tenantId,
        GetCreditBootstrapRequest request,
        CancellationToken ct)
    {
        try
        {
            if (request is null)
                throw new ArgumentNullException(nameof(request));

            var license = await _licenseRuntime.GetRuntimeContextAsync(tenantId, request.License, ct).ConfigureAwait(false);
            var credits = request.Credits is null
                ? null
                : await _creditSummaries.GetRuntimeSummaryAsync(tenantId, request.Credits, ct).ConfigureAwait(false);

            return OkResponse(new CreditBootstrapInfo(license, credits, true));
        }
        catch (LicensingException ex)
        {
            return BadRequestResponse(ex.Message);
        }
        catch (CreditException ex)
        {
            return BadRequestResponse(ex.Message);
        }
    }
}
