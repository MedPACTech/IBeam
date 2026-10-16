using IBeam.Api.Controllers;
using IBeam.Management.Api;
using IBeam.Management.Identity.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.Management.Identity.Api;

[Authorize(Policy = ManagementApiPolicies.Access)]
[ServiceFilter(typeof(ManagementIdentityExceptionFilter))]
[Route("api/management/support-users")]
public sealed class ManagementSupportAccessController(ManagementSupportAccessService access) : ApiControllerBase
{
    [HttpPost("{userId:guid}/disable")]
    public async Task<IActionResult> DisableAsync(Guid userId, CancellationToken ct)
    {
        await access.DisableAsync(User, userId, ct);
        return NoContentResponse();
    }
}
