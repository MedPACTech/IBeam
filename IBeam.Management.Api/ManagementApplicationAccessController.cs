using IBeam.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace IBeam.Management.Api;

[NonController]
[Authorize(Policy = ManagementApiPolicies.Access)]
[ServiceFilter(typeof(ManagementApplicationApiFilter), Order = -3000)]
[Route("api/management/applications/{applicationId:guid}/support-users")]
public sealed class ManagementApplicationAccessController(IManagementApplicationAccessService service) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> ListAsync(Guid applicationId, [FromQuery] int pageSize = 50,
        [FromQuery] string? continuationToken = null, CancellationToken ct = default)
    {
        var page = await service.ListAsync(applicationId, pageSize, continuationToken, ct);
        return OkCursorPagedResponse(page.Items, page.PageSize, page.ContinuationToken);
    }
    [HttpPut("{userId:guid}")]
    public async Task<IActionResult> PutAsync(Guid applicationId, Guid userId, ManagementApplicationAccessRequest request, CancellationToken ct)
        => OkResponse(await service.PutAsync(applicationId, userId, request, ct));
    [HttpDelete("{userId:guid}")]
    public async Task<IActionResult> RevokeAsync(Guid applicationId, Guid userId, [FromHeader(Name = "If-Match")] string expectedVersion, CancellationToken ct)
    {
        await service.RevokeAsync(applicationId, userId, expectedVersion, ct);
        return NoContentResponse();
    }
}
