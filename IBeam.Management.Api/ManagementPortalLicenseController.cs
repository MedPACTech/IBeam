using IBeam.Api.Controllers;
using IBeam.Management.Licensing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.Management.Api;

[NonController, Authorize(Policy = ManagementApiPolicies.Access), HttpLogging(HttpLoggingFields.None)]
[ServiceFilter(typeof(ManagementApplicationApiFilter), Order = -3000)]
[Route("api/management/applications/{applicationId:guid}/tenants/{tenantId:guid}/licenses/{licenseId:guid}")]
public sealed class ManagementPortalLicenseController(IManagementLicensePortalService service) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Read(Guid applicationId, Guid tenantId, Guid licenseId, CancellationToken ct)
        => OkResponse(await service.ReadAsync(applicationId, tenantId, licenseId, ct));
    [HttpPost("suspend")]
    public async Task<IActionResult> Suspend(Guid applicationId, Guid tenantId, Guid licenseId, [FromBody] ManagementLicenseActionRequest request,
        [FromHeader(Name = "If-Match")] string version, [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct)
        => ResponseFor(applicationId, tenantId, licenseId, ManagementLicenseOperations.Suspend,
            await service.SuspendAsync(applicationId, tenantId, new(licenseId, version), key, ct));
    [HttpPost("restore")]
    public async Task<IActionResult> Restore(Guid applicationId, Guid tenantId, Guid licenseId, [FromBody] ManagementLicenseActionRequest request,
        [FromHeader(Name = "If-Match")] string version, [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct)
        => ResponseFor(applicationId, tenantId, licenseId, ManagementLicenseOperations.Restore,
            await service.RestoreAsync(applicationId, tenantId, new(licenseId, version), key, ct));
    [HttpGet("operations/{operationId:guid}")]
    public async Task<IActionResult> Find(Guid applicationId, Guid tenantId, Guid licenseId, Guid operationId, [FromQuery] string operation, CancellationToken ct)
        => OkResponse(await service.FindAsync(applicationId, tenantId, licenseId, operationId, operation, ct));
    private IActionResult ResponseFor(Guid applicationId, Guid tenantId, Guid licenseId, string operation, ManagementLicenseTransitionResult result)
    {
        Response.Headers.Location = Url.Action(nameof(Find), new { applicationId, tenantId, licenseId, operationId = result.OperationId, operation });
        var response = (ObjectResult)OkResponse(result); response.StatusCode = result.Outcome == "succeeded" ? 200 : 202; return response;
    }
}
