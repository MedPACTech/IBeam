using System.Security.Claims;
using IBeam.Api.Controllers;
using IBeam.Billing.Licensing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.Billing.Api;

[Authorize(Policy = BillingApiServiceCollectionExtensions.CommerceAdministrationPolicy)]
[Route("api/billing/commerce/tenants/{tenantId:guid}")]
public sealed class CommerceAdministrationController : ApiControllerBase
{
    private readonly ICommerceAdministrationService _administration;

    public CommerceAdministrationController(ICommerceAdministrationService administration)
    {
        _administration = administration;
    }

    [HttpGet("purchases/{purchaseId:guid}")]
    public Task<IActionResult> InspectAsync(Guid tenantId, Guid purchaseId, CancellationToken ct)
        => ExecuteAsync(tenantId, () => _administration.InspectAsync(tenantId, purchaseId, ct));

    [HttpPost("provider-events/{providerName}/{providerEventId}/retry")]
    public Task<IActionResult> RetryVerifiedEventAsync(
        Guid tenantId,
        string providerName,
        string providerEventId,
        [FromBody] CommerceRecoveryRequest request,
        CancellationToken ct)
        => ExecuteAsync(tenantId, () => _administration.RetryVerifiedEventAsync(tenantId, providerName, providerEventId, request, ct));

    [HttpPost("purchases/{purchaseId:guid}/retry-fulfillment")]
    public Task<IActionResult> RetryFulfillmentAsync(
        Guid tenantId,
        Guid purchaseId,
        [FromBody] CommerceRecoveryRequest request,
        CancellationToken ct)
        => ExecuteAsync(tenantId, () => _administration.RetryFulfillmentAsync(tenantId, purchaseId, request, ct));

    [HttpPost("purchases/{purchaseId:guid}/resend-claim")]
    public Task<IActionResult> ResendClaimAsync(
        Guid tenantId,
        Guid purchaseId,
        [FromBody] CommerceRecoveryRequest request,
        CancellationToken ct)
        => ExecuteAsync(tenantId, () => _administration.ResendClaimAsync(tenantId, purchaseId, request, ct));

    [HttpPost("purchases/{purchaseId:guid}/corrections")]
    public Task<IActionResult> ApplyManualCorrectionAsync(
        Guid tenantId,
        Guid purchaseId,
        [FromBody] ManualCommerceCorrectionRequest request,
        CancellationToken ct)
        => ExecuteAsync(tenantId, () => _administration.ApplyManualCorrectionAsync(tenantId, purchaseId, request, ct));

    private async Task<IActionResult> ExecuteAsync<T>(Guid tenantId, Func<Task<T>> operation)
    {
        if (!HasRequestedTenant(tenantId))
            return Forbid();

        try
        {
            return OkResponse(await operation().ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is BillingException or ArgumentException)
        {
            return BadRequestResponse(ex.Message);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    private bool HasRequestedTenant(Guid tenantId)
    {
        var rawTenantId = User.FindFirstValue("tid")
            ?? User.FindFirstValue("tenant_id")
            ?? User.FindFirstValue("http://schemas.microsoft.com/identity/claims/tenantid");
        return Guid.TryParse(rawTenantId, out var authenticatedTenantId) && authenticatedTenantId == tenantId;
    }
}
