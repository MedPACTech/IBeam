using System.ComponentModel.DataAnnotations;
using IBeam.Api.Controllers;
using IBeam.Management.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IBeam.Management.Diagnostics.Api.Controllers;

// Added explicitly by the optional log API feature provider. Base API assembly scanning must not expose this route.
[NonController]
[Authorize(Policy = ManagementDiagnosticsApiPolicies.Access)]
[ServiceFilter(typeof(ManagementDiagnosticApiFilter), Order = -3000)]
[Route("api/management-diagnostics/system-logs")]
public sealed class SystemLogsController(ISystemLogReaderService service) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> ListAsync([FromQuery] SystemLogListRequest request, CancellationToken ct)
    {
        var page = await service.ListAsync(new SystemLogQuery
        {
            FromUtc = request.FromUtc, ToUtc = request.ToUtc, PageSize = request.PageSize,
            Source = request.Source, Level = request.Level, TraceId = request.TraceId, Cursor = request.ContinuationToken
        }, ct);
        return OkCursorPagedResponse(page.Items, page.PageSize, page.ContinuationToken);
    }

    [HttpGet("{logId}")]
    public async Task<IActionResult> GetAsync([FromRoute, StringLength(16384)] string logId, CancellationToken ct)
        => OkResponse(await service.GetAsync(logId, ct));
}

public sealed class SystemLogListRequest
{
    public DateTimeOffset? FromUtc { get; set; }
    public DateTimeOffset? ToUtc { get; set; }
    public int? PageSize { get; set; }
    [StringLength(128)] public string? Source { get; set; }
    [StringLength(32)] public string? Level { get; set; }
    [StringLength(128)] public string? TraceId { get; set; }
    [StringLength(16384)] public string? ContinuationToken { get; set; }
    public override string ToString() => "System log list request (filters and continuation redacted)";
}
