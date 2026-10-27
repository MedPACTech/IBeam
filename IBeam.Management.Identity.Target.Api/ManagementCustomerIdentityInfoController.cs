using IBeam.Api.Controllers;
using IBeam.Management.CustomerIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
namespace IBeam.Management.Identity.Target.Api;

[NonController]
[Authorize(Policy = ManagementCustomerIdentityTargetApiExtensions.Policy)]
[ServiceFilter(typeof(ManagementCustomerIdentityTargetFilter))]
[HttpLogging(HttpLoggingFields.None)]
[Route("api/management-identity")]
public sealed class ManagementCustomerIdentityInfoController(IManagementCustomerIdentityInfoService service) : ApiControllerBase
{
    [HttpGet("info")]
    public async Task<IActionResult> GetAsync(CancellationToken ct) => OkResponse(await service.GetAsync(ct));
}
