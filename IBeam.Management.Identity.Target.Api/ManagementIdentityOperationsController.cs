using IBeam.Api.Controllers;
using IBeam.Management.CustomerIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
namespace IBeam.Management.Identity.Target.Api;

[NonController]
[Authorize(Policy = ManagementCustomerIdentityTargetApiExtensions.Policy)]
[ServiceFilter(typeof(ManagementCustomerIdentityTargetFilter), Order = -3000)]
[HttpLogging(HttpLoggingFields.None)]
[Route("api/management-identity/operations")]
public sealed class ManagementIdentityOperationsController(IManagementIdentityOperationQueryService service, IManagementDelegatedActorContext actors) : ApiControllerBase
{
    [HttpGet("{operationId:guid}"), ActionName("GetIdentityOperation"), ManagementIdentityOperationReceipt]
    public async Task<IActionResult> GetAsync(Guid operationId, CancellationToken ct)
        => OkResponse(await service.GetAsync(operationId, actors.Current?.Operation ?? "", ct));
}
