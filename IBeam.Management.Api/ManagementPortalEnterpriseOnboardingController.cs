using IBeam.Api.Controllers;
using IBeam.Management.CustomerIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
namespace IBeam.Management.Api;
[NonController, Authorize(Policy = ManagementApiPolicies.Access), HttpLogging(HttpLoggingFields.None)]
[ServiceFilter(typeof(ManagementApplicationApiFilter), Order = -3000)]
[Route("api/management/applications/{applicationId:guid}/enterprise-onboardings")]
public sealed class ManagementPortalEnterpriseOnboardingController(IManagementPortalEnterpriseOnboardingService service) : ApiControllerBase
{
    [HttpGet("policies")]
    public async Task<IActionResult> Policies(Guid applicationId, CancellationToken ct) => OkResponse(await service.ListPoliciesAsync(applicationId, ct));
    [HttpPost]
    public async Task<IActionResult> Create(Guid applicationId, [FromBody] CreateManagementEnterpriseOnboarding request, [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct)
    { var result = await service.CreateAsync(applicationId, request, key, ct); return Reply(applicationId, ManagementCustomerIdentityOperations.OnboardingCreate, result); }
    [HttpPost("{operationId:guid}/retry")]
    public async Task<IActionResult> Retry(Guid applicationId, Guid operationId, [FromBody] RetryManagementEnterpriseOnboarding request, [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct)
    { var result = await service.RetryAsync(applicationId, operationId, key, ct); return Reply(applicationId, ManagementCustomerIdentityOperations.OnboardingRetry, result); }
    private IActionResult Reply(Guid applicationId, string authority, ManagementPortalOperationResult result)
    { Response.Headers.Location = Url.Action("GetManagementMutation", new { controller = "ManagementPortalMutationOperations", applicationId, reference = result.Reference, authority });
      var response = (ObjectResult)OkResponse(result); response.StatusCode = result.Operation?.Onboarding?.Completed == true ? 200 : 202; return response; }
}
