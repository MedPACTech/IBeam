using IBeam.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.Billing.Api;

[Authorize]
[Route("api/billing/provider-events")]
public sealed class BillingProviderEventsController : ApiControllerBase
{
    private readonly IBillingProviderEventService _events;

    public BillingProviderEventsController(IBillingProviderEventService events)
    {
        _events = events;
    }

    [HttpPost]
    public async Task<IActionResult> RecordEventAsync(RecordBillingProviderEventRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _events.RecordEventAsync(request, ct).ConfigureAwait(false);
            return OkResponse(result);
        }
        catch (BillingException ex)
        {
            return BadRequestResponse(ex.Message);
        }
    }
}
