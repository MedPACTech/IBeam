using IBeam.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.Billing.Api;

[Authorize]
[Route("api/billing/tenants/{tenantId:guid}")]
public sealed class BillingAdminController : ApiControllerBase
{
    private readonly IBillingCustomerService _customers;
    private readonly IBillingSubscriptionService _subscriptions;
    private readonly IBillingInvoiceService _invoices;
    private readonly IBillingProviderEventService _events;

    public BillingAdminController(
        IBillingCustomerService customers,
        IBillingSubscriptionService subscriptions,
        IBillingInvoiceService invoices,
        IBillingProviderEventService events)
    {
        _customers = customers;
        _subscriptions = subscriptions;
        _invoices = invoices;
        _events = events;
    }

    [HttpGet("customers")]
    public async Task<IActionResult> ListCustomersAsync(Guid tenantId, CancellationToken ct)
    {
        try
        {
            var result = await _customers.ListCustomersAsync(tenantId, ct).ConfigureAwait(false);
            return OkResponse(result);
        }
        catch (BillingException ex)
        {
            return BadRequestResponse(ex.Message);
        }
    }

    [HttpGet("subscriptions")]
    public async Task<IActionResult> ListSubscriptionsAsync(Guid tenantId, CancellationToken ct)
    {
        try
        {
            var result = await _subscriptions.ListSubscriptionsAsync(tenantId, ct).ConfigureAwait(false);
            return OkResponse(result);
        }
        catch (BillingException ex)
        {
            return BadRequestResponse(ex.Message);
        }
    }

    [HttpGet("invoices")]
    public async Task<IActionResult> ListInvoicesAsync(Guid tenantId, CancellationToken ct)
    {
        try
        {
            var result = await _invoices.ListInvoicesAsync(tenantId, ct).ConfigureAwait(false);
            return OkResponse(result);
        }
        catch (BillingException ex)
        {
            return BadRequestResponse(ex.Message);
        }
    }

    [HttpGet("provider-events")]
    public async Task<IActionResult> ListProviderEventsAsync(Guid tenantId, CancellationToken ct)
    {
        try
        {
            var result = await _events.ListEventsAsync(tenantId, ct).ConfigureAwait(false);
            return OkResponse(result);
        }
        catch (BillingException ex)
        {
            return BadRequestResponse(ex.Message);
        }
    }
}
