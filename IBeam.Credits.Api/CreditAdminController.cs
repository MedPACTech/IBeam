using IBeam.Api.Controllers;
using IBeam.Credits.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.Credits.Api;

[Authorize]
[Route("api/credits/tenants/{tenantId:guid}/admin")]
public sealed class CreditAdminController : ApiControllerBase
{
    private readonly ICreditBalanceSummaryService _summaries;

    public CreditAdminController(ICreditBalanceSummaryService summaries)
    {
        _summaries = summaries;
    }

    [HttpGet("ledger")]
    public async Task<IActionResult> ListLedgerEntriesAsync(
        Guid tenantId,
        [FromQuery] Guid creditAccountId,
        [FromQuery] string? bucketKey,
        CancellationToken ct)
    {
        try
        {
            var result = await _summaries.ListLedgerEntriesAsync(
                tenantId,
                new ListCreditLedgerEntriesRequest
                {
                    CreditAccountId = creditAccountId,
                    BucketKey = bucketKey
                },
                ct).ConfigureAwait(false);
            return OkResponse(result);
        }
        catch (CreditException ex)
        {
            return BadRequestResponse(ex.Message);
        }
    }

    [HttpGet("reservations")]
    public async Task<IActionResult> ListReservationsAsync(
        Guid tenantId,
        [FromQuery] Guid? creditAccountId,
        [FromQuery] string? bucketKey,
        CancellationToken ct)
    {
        try
        {
            var result = await _summaries.ListReservationsAsync(
                tenantId,
                new ListCreditReservationsRequest
                {
                    CreditAccountId = creditAccountId,
                    BucketKey = bucketKey
                },
                ct).ConfigureAwait(false);
            return OkResponse(result);
        }
        catch (CreditException ex)
        {
            return BadRequestResponse(ex.Message);
        }
    }
}
