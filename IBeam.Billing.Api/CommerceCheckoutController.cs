using IBeam.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IBeam.Billing.Api;

[ApiController]
[AllowAnonymous]
[EnableRateLimiting(BillingApiServiceCollectionExtensions.PublicCheckoutRateLimitPolicy)]
[Route("api/commerce")]
public sealed class CommerceCheckoutController : ApiControllerBase
{
    private readonly IBillingPublicCheckoutService _checkout;

    public CommerceCheckoutController(IBillingPublicCheckoutService checkout)
    {
        _checkout = checkout;
    }

    [HttpPost("checkout-sessions")]
    public async Task<IActionResult> StartCheckoutAsync(StartBillingCheckoutRequest request, CancellationToken ct)
    {
        try
        {
            return OkResponse(await _checkout.StartCheckoutAsync(request, ct).ConfigureAwait(false));
        }
        catch (BillingException ex)
        {
            return BadRequestResponse(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return BadRequestResponse(ex.Message);
        }
    }

    [HttpGet("purchases/{purchaseId:guid}/status")]
    public async Task<IActionResult> GetPurchaseStatusAsync(
        Guid purchaseId,
        [FromQuery] string token,
        CancellationToken ct)
    {
        var status = await _checkout.GetPurchaseStatusAsync(purchaseId, token, ct).ConfigureAwait(false);
        return status is null ? NotFound() : OkResponse(status);
    }
}
