using IBeam.Api.Controllers;
using IBeam.Management.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace IBeam.Management.Api;
[NonController, Authorize(Policy = ManagementApiPolicies.Access)]
[ServiceFilter(typeof(ManagementApplicationApiFilter), Order = -3000)]
[Route("api/management/applications/{applicationId:guid}")]
public sealed class ManagementPortalDiagnosticsController(IManagementDiagnosticsService service) : ApiControllerBase
{
    [HttpGet("system-errors")]
    public async Task<IActionResult> ErrorsAsync(Guid applicationId, [FromQuery] PortalDiagnosticRequest query, CancellationToken ct)
    {
        var page = await service.ErrorsAsync(applicationId, new() { FromUtc = query.FromUtc, ToUtc = query.ToUtc,
            PageSize = query.PageSize, Cursor = query.ContinuationToken }, ct);
        return OkCursorPagedResponse(page.Items, page.PageSize, page.ContinuationToken);
    }
    [HttpGet("system-errors/{id}")]
    public async Task<IActionResult> ErrorAsync(Guid applicationId, string id, CancellationToken ct)
        => OkResponse(await service.ErrorAsync(applicationId, id, ct));
    [HttpGet("system-logs")]
    public async Task<IActionResult> LogsAsync(Guid applicationId, [FromQuery] PortalDiagnosticRequest query, CancellationToken ct)
    {
        var page = await service.LogsAsync(applicationId, new() { FromUtc = query.FromUtc, ToUtc = query.ToUtc, PageSize = query.PageSize,
            Cursor = query.ContinuationToken, Source = query.Source, Level = query.Level, TraceId = query.TraceId }, ct);
        return OkCursorPagedResponse(page.Items, page.PageSize, page.ContinuationToken);
    }
    [HttpGet("system-logs/{id}")]
    public async Task<IActionResult> LogAsync(Guid applicationId, string id, CancellationToken ct)
        => OkResponse(await service.LogAsync(applicationId, id, ct));
}
public sealed class PortalDiagnosticRequest
{
    public DateTimeOffset? FromUtc { get; set; }
    public DateTimeOffset? ToUtc { get; set; }
    public int? PageSize { get; set; }
    [System.ComponentModel.DataAnnotations.StringLength(16384)] public string? ContinuationToken { get; set; }
    public string? Source { get; set; }
    public string? Level { get; set; }
    public string? TraceId { get; set; }
    public override string ToString() => "Portal diagnostic request (state redacted)";
}
