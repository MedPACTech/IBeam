using System.ComponentModel.DataAnnotations;
using IBeam.Api.Controllers;
using IBeam.Management.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.Management.Diagnostics.Api.Controllers;

// Added explicitly by the optional error API feature provider. Base API assembly scanning must not expose this route.
[NonController]
[Authorize(Policy = ManagementDiagnosticsApiPolicies.Access)]
[ServiceFilter(typeof(ManagementDiagnosticApiFilter), Order = -3000)]
[Route("api/management-diagnostics/system-errors")]
public sealed class SystemErrorsController(ISystemErrorReaderService service) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> ListAsync([FromQuery] SystemErrorListRequest request, CancellationToken ct)
    {
        var page = await service.ListAsync(new DiagnosticQuery
        {
            FromUtc = request.FromUtc, ToUtc = request.ToUtc, PageSize = request.PageSize, Cursor = request.ContinuationToken
        }, ct);
        return OkCursorPagedResponse(page.Items, page.PageSize, page.ContinuationToken);
    }

    [HttpGet("{errorId}")]
    public async Task<IActionResult> GetAsync([FromRoute, StringLength(16384)] string errorId, CancellationToken ct)
        => OkResponse(await service.GetAsync(errorId, ct));
}

public sealed class SystemErrorListRequest
{
    public DateTimeOffset? FromUtc { get; set; }
    public DateTimeOffset? ToUtc { get; set; }
    public int? PageSize { get; set; }
    [StringLength(16384)] public string? ContinuationToken { get; set; }
    public override string ToString() => "System error list request (continuation redacted)";
}
