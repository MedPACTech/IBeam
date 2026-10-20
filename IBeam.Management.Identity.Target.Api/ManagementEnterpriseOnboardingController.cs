using IBeam.Api.Controllers;
using IBeam.Management.CustomerIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
namespace IBeam.Management.Identity.Target.Api;

[NonController, Authorize(Policy = ManagementCustomerIdentityTargetApiExtensions.Policy), HttpLogging(HttpLoggingFields.None)]
[ServiceFilter(typeof(ManagementCustomerIdentityTargetFilter), Order = -3000)]
[Route("api/management-identity/enterprise-onboardings")]
public sealed class ManagementEnterpriseOnboardingController(IManagementEnterpriseOnboardingCommandService service) : ApiControllerBase
{
    [HttpPost, ManagementCustomerIdentityOperation(ManagementCustomerIdentityOperations.OnboardingCreate)]
    public async Task<IActionResult> Create([FromBody] CreateManagementEnterpriseOnboarding request, [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct)
    { var result = await service.CreateAsync(request, key, ct); return Reply(result); }
    [HttpPost("{operationId:guid}/retry"), ManagementCustomerIdentityOperation(ManagementCustomerIdentityOperations.OnboardingRetry)]
    public async Task<IActionResult> Retry(Guid operationId, [FromBody] RetryManagementEnterpriseOnboarding request, [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct)
    { var result = await service.RetryAsync(operationId, key, ct); return Reply(result); }
    private IActionResult Reply(ManagementIdentityOperationDto result)
    { Response.Headers.Location = Url.Action("GetIdentityOperation", new { controller = "ManagementIdentityOperations", operationId = result.OperationId });
      var response = (ObjectResult)OkResponse(result); response.StatusCode = result.Onboarding?.Completed == true ? 200 : 202; return response; }
}
