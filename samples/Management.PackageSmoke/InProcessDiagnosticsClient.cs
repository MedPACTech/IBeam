using System.Net;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.Models;
// In-process TestServer transport adapter used only by this executable. Production uses ApplicationDiagnosticsClient's HTTPS/DNS-pinning policy.
internal sealed class InProcessDiagnosticsClient(IReadOnlyDictionary<string, SmokeTarget> targets, IManagementAuditContext audit) : IApplicationDiagnosticsClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private async Task<JsonElement> Get(ManagementDiagnosticConnection c, string path, CancellationToken ct)
    {
        if (!targets.TryGetValue(c.BaseUrl, out var target)) throw new ManagementAccessDeniedException();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/management-diagnostics/" + path);
        request.Headers.Add("X-IBeam-Management-Key", c.Key.Value); request.Headers.Add("X-IBeam-Management-Correlation", audit.CorrelationId);
        using var response = await target.Client.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.Unauthorized) throw new ManagementAccessDeniedException();
        if (response.StatusCode == HttpStatusCode.NotFound) throw new ManagementDiagnosticNotFoundException();
        if (response.StatusCode != HttpStatusCode.OK) throw new ManagementDiagnosticUnavailableException();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)); return json.RootElement.Clone();
    }
    public async Task<ManagementDiagnosticsInfo> InfoAsync(ManagementDiagnosticConnection c, CancellationToken ct = default)
        => (await Get(c, "info", ct)).GetProperty("data").Deserialize<ManagementDiagnosticsInfo>(Json)!;
    public async Task<SystemErrorPage> ErrorsAsync(ManagementDiagnosticConnection c, DiagnosticQuery q, CancellationToken ct = default)
    { var json = await Get(c, "system-errors" + Query(q.FromUtc, q.ToUtc, q.PageSize, q.Cursor), ct); return new(json.GetProperty("data").Deserialize<SystemErrorDto[]>(Json)!, json.GetProperty("pageSize").GetInt32(), json.GetProperty("continuationToken").GetString()); }
    public async Task<SystemErrorDto> ErrorAsync(ManagementDiagnosticConnection c, string id, CancellationToken ct = default)
        => (await Get(c, "system-errors/" + Uri.EscapeDataString(id), ct)).GetProperty("data").Deserialize<SystemErrorDto>(Json)!;
    public async Task<SystemLogPage> LogsAsync(ManagementDiagnosticConnection c, SystemLogQuery q, CancellationToken ct = default)
    { var json = await Get(c, "system-logs" + Query(q.FromUtc, q.ToUtc, q.PageSize, q.Cursor), ct); return new(json.GetProperty("data").Deserialize<SystemLogDto[]>(Json)!, json.GetProperty("pageSize").GetInt32(), json.GetProperty("continuationToken").GetString()); }
    public async Task<SystemLogDto> LogAsync(ManagementDiagnosticConnection c, string id, CancellationToken ct = default)
        => (await Get(c, "system-logs/" + Uri.EscapeDataString(id), ct)).GetProperty("data").Deserialize<SystemLogDto>(Json)!;
    private static string Query(DateTimeOffset? from, DateTimeOffset? to, int? size, string? cursor)
    {
        var values = new List<KeyValuePair<string, string>>();
        if (from.HasValue) values.Add(new("fromUtc", from.Value.ToString("O"))); if (to.HasValue) values.Add(new("toUtc", to.Value.ToString("O")));
        if (size.HasValue) values.Add(new("pageSize", size.Value.ToString())); if (cursor is not null) values.Add(new("continuationToken", cursor));
        return "?" + string.Join("&", values.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value)));
    }
}
internal sealed class SmokeProbe(IApplicationDiagnosticsClient client) : IManagementTargetConnectionProbe
{
    public Task<ManagementDiagnosticsInfo> ProbeAsync(string origin, ManagementSecretValue key, CancellationToken ct = default)
        => client.InfoAsync(new() { BaseUrl = origin, Key = key }, ct);
}
