using IBeam.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.Billing.Api;

[AllowAnonymous]
[Route("api/billing/webhooks/{providerName}")]
public sealed class BillingWebhooksController : ApiControllerBase
{
    private const int MaxWebhookBytes = 1024 * 1024;
    private readonly IBillingWebhookProcessor _processor;

    public BillingWebhooksController(IBillingWebhookProcessor processor)
    {
        _processor = processor;
    }

    [HttpPost]
    public async Task<IActionResult> ReceiveAsync(string providerName, CancellationToken ct)
    {
        if (Request.ContentLength > MaxWebhookBytes)
            return BadRequestResponse("Webhook payload is too large.");

        await using var body = new MemoryStream();
        var buffer = new byte[81920];
        int bytesRead;
        while ((bytesRead = await Request.Body.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            if (body.Length + bytesRead > MaxWebhookBytes)
                return BadRequestResponse("Webhook payload is too large.");
            await body.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
        }
        if (body.Length == 0)
            return BadRequestResponse("Webhook payload is required.");

        try
        {
            var headers = Request.Headers.ToDictionary(
                x => x.Key,
                x => x.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);
            var result = await _processor.ProcessAsync(
                providerName,
                BillingWebhookRequest.Create(body.ToArray(), headers),
                ct).ConfigureAwait(false);
            return OkResponse(result);
        }
        catch (BillingException ex)
        {
            return BadRequestResponse(ex.Message, code: "WebhookRejected");
        }
        catch (ArgumentException ex)
        {
            return BadRequestResponse(ex.Message, code: "WebhookRejected");
        }
    }
}
