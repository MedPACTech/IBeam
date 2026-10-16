using IBeam.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.Management.Api;
[NonController]
[Authorize(Policy = ManagementApiPolicies.Access)]
[ServiceFilter(typeof(ManagementApplicationApiFilter), Order = -3000)]
[Route("api/management/applications")]
public sealed class ManagementApplicationsController(IManagementApplicationService service, IManagementApplicationSelector? selector = null) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> ListAsync([FromQuery] int pageSize = 50, [FromQuery] string? continuationToken = null, CancellationToken ct = default)
    {
        if (selector is not null)
        {
            var selected = await selector.ListAsync(pageSize, continuationToken, ct);
            return OkCursorPagedResponse(selected.Items, selected.PageSize, selected.ContinuationToken);
        }
        var page = await service.ListAsync(pageSize, continuationToken, ct);
        return OkCursorPagedResponse(page.Items, page.PageSize, page.ContinuationToken);
    }
    [HttpGet("registry")]
    public async Task<IActionResult> RegistryAsync([FromQuery] int pageSize = 50, [FromQuery] string? continuationToken = null, CancellationToken ct = default)
    {
        var page = await service.ListAsync(pageSize, continuationToken, ct);
        return OkCursorPagedResponse(page.Items, page.PageSize, page.ContinuationToken);
    }
    [HttpGet("{applicationId:guid}"), ActionName("GetApplication")]
    public async Task<IActionResult> GetAsync(Guid applicationId, CancellationToken ct) => OkResponse(await service.GetAsync(applicationId, ct));
    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] CreateManagementApplication request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return CreatedResponse("GetApplication", new { applicationId = result.ApplicationId }, result);
    }
    [HttpPatch("{applicationId:guid}")]
    public async Task<IActionResult> UpdateAsync(Guid applicationId, [FromBody] UpdateManagementApplication request, CancellationToken ct)
        => OkResponse(await service.UpdateAsync(applicationId, request, ct));
}
