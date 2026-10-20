using IBeam.Api.Controllers;
using IBeam.Management.CustomerIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
namespace IBeam.Management.Identity.Target.Api;

[NonController, Authorize(Policy = ManagementCustomerIdentityTargetApiExtensions.Policy), HttpLogging(HttpLoggingFields.None)]
[ServiceFilter(typeof(ManagementCustomerIdentityTargetFilter), Order = -3000)]
[Route("api/management-identity/operations/resolve")]
public sealed class ManagementIdentityOperationLookupController(IManagementIdentityOperationLookupService service, IManagementDelegatedActorContext actors) : ApiControllerBase
{
    [HttpPost, ManagementIdentityOperationReceipt]
    public async Task<IActionResult> ResolveAsync([FromBody] ManagementIdentityOperationLookup request, [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct)
        => OkResponse(await service.ResolveAsync(request, key, actors.Current?.Operation ?? "", ct));
}
