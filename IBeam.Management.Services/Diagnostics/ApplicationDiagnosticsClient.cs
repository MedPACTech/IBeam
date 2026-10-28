using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using IBeam.Management.Models;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Services.Diagnostics;

/// <summary>Fixed diagnostic GET paths, bounded envelopes and a second safe DTO projection. Never proxies raw bodies or response headers.</summary>
public sealed class ApplicationDiagnosticsClient(ManagementConnectionNetworkPolicy network,
    IOptions<PortalDiagnosticsOptions> options, IManagementAuditContext? audit = null) : IApplicationDiagnosticsClient
{
    internal Func<HttpMessageHandler>? TestTransportFactory { get; init; }
    public async Task<ManagementDiagnosticsInfo> InfoAsync(ManagementDiagnosticConnection c, CancellationToken ct = default)
    {
        using var body = await GetAsync(c, "info", [], ct);
        var info = Deserialize<ManagementDiagnosticsInfo>(body.RootElement) ?? throw new ManagementPortalResponseException();
        try { ManagementConnectionProtocol.Validate(info); } catch { throw new ManagementPortalResponseException(); }
        if (info.ApplicationKey != c.ApplicationKey || info.Environment != c.Environment) throw new ManagementPortalResponseException();
        return info;
    }
    public async Task<SystemErrorPage> ErrorsAsync(ManagementDiagnosticConnection c, DiagnosticQuery query, CancellationToken ct = default)
    {
        using var body = await GetAsync(c, "system-errors", Query(query), ct);
        var root = body.RootElement;
        var rows = Deserialize<SystemErrorDto[]>(root) ?? throw new ManagementPortalResponseException();
        var size = PageSize(root, rows.Length, query.PageSize);
        return new(rows.Select(x => SafeError(x, c.AllowUnsanitized)).ToArray(), size, Continuation(root));
    }
    public async Task<SystemErrorDto> ErrorAsync(ManagementDiagnosticConnection c, string id, CancellationToken ct = default)
    {
        CheckOpaque(id);
        using var body = await GetAsync(c, "system-errors/" + Uri.EscapeDataString(id), [], ct);
        var row = Deserialize<SystemErrorDto>(body.RootElement) ?? throw new ManagementPortalResponseException();
        if (row.Id != id) throw new ManagementPortalResponseException();
        return SafeError(row, c.AllowUnsanitized);
    }
    public async Task<SystemLogPage> LogsAsync(ManagementDiagnosticConnection c, SystemLogQuery query, CancellationToken ct = default)
    {
        var parameters = Query(new() { FromUtc = query.FromUtc, ToUtc = query.ToUtc, PageSize = query.PageSize, Cursor = query.Cursor });
        if (query.Source is not null) parameters.Add(new("source", query.Source));
        if (query.Level is not null) parameters.Add(new("level", query.Level));
        if (query.TraceId is not null) parameters.Add(new("traceId", query.TraceId));
        using var body = await GetAsync(c, "system-logs", parameters, ct);
        var root = body.RootElement;
        var rows = Deserialize<SystemLogDto[]>(root) ?? throw new ManagementPortalResponseException();
        var size = PageSize(root, rows.Length, query.PageSize);
        return new(rows.Select(x => SafeLog(x, c.AllowUnsanitized)).ToArray(), size, Continuation(root));
    }
    public async Task<SystemLogDto> LogAsync(ManagementDiagnosticConnection c, string id, CancellationToken ct = default)
    {
        CheckOpaque(id);
        using var body = await GetAsync(c, "system-logs/" + Uri.EscapeDataString(id), [], ct);
        var row = Deserialize<SystemLogDto>(body.RootElement) ?? throw new ManagementPortalResponseException();
        if (row.Id != id) throw new ManagementPortalResponseException();
        return SafeLog(row, c.AllowUnsanitized);
    }
    private static T? Deserialize<T>(JsonElement root)
    {
        try
        {
            if (!root.TryGetProperty("data", out var data)) throw new ManagementPortalResponseException();
            return data.Deserialize<T>(JsonOptions);
        }
        catch { throw new ManagementPortalResponseException(); }
    }
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private Task<JsonDocument> GetAsync(ManagementDiagnosticConnection c, string path, List<KeyValuePair<string, string>> query, CancellationToken ct)
        => ManagementTargetTelemetry.Run("diagnostics", "read", path == "info" ? ManagementOperations.DiagnosticsInfo :
            path.StartsWith("system-errors", StringComparison.Ordinal) ? ManagementOperations.ErrorsRead : ManagementOperations.LogsRead,
            c.ApplicationId, null, null, audit, ct, async () => {
        try
        {
            var origin = network.RequireOrigin(c.BaseUrl);
            using var client = ManagementTargetHttpTransport.CreateClient(network, origin, TestTransportFactory);
            var suffix = query.Count == 0 ? "" : "?" + string.Join("&", query.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value)));
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(origin, "/api/management-diagnostics/" + path + suffix))
                { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
            request.Headers.Add("X-IBeam-Management-Key", c.Key.Value);
            if (audit is not null) request.Headers.Add("X-IBeam-Management-Correlation", audit.CorrelationId);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new ManagementPortalRateLimitException();
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new ManagementAccessDeniedException();
            if (response.StatusCode == HttpStatusCode.NotFound) throw new ManagementDiagnosticNotFoundException();
            if (response.StatusCode == HttpStatusCode.BadRequest) throw new DiagnosticQueryException();
            if ((int)response.StatusCode >= 500) throw new ManagementDiagnosticUnavailableException();
            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength > options.Value.MaximumResponseBytes)
                throw new ManagementPortalResponseException();
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[4096];
            while (true)
            {
                var count = await stream.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, options.Value.MaximumResponseBytes + 1 - buffer.Length)), ct);
                if (count == 0) break;
                buffer.Write(chunk, 0, count);
                if (buffer.Length > options.Value.MaximumResponseBytes) throw new ManagementPortalResponseException();
            }
            var json = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 12 });
            if (!json.RootElement.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)
            { json.Dispose(); throw new ManagementPortalResponseException(); }
            return json;
        }
        catch (OperationCanceledException) { throw new OperationCanceledException("Portal diagnostic request was cancelled.", ct); }
        catch (Exception ex) when (ex is ManagementAccessDeniedException or ManagementDiagnosticNotFoundException or DiagnosticQueryException or
            ManagementPortalResponseException or ManagementPortalRateLimitException or ManagementDiagnosticUnavailableException) { throw; }
        catch { throw new ManagementDiagnosticUnavailableException(); }
    });
    private static List<KeyValuePair<string, string>> Query(DiagnosticQuery q)
    {
        var values = new List<KeyValuePair<string, string>>();
        if (q.FromUtc.HasValue) values.Add(new("fromUtc", q.FromUtc.Value.ToString("O")));
        if (q.ToUtc.HasValue) values.Add(new("toUtc", q.ToUtc.Value.ToString("O")));
        if (q.PageSize.HasValue) values.Add(new("pageSize", q.PageSize.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (q.Cursor is not null) { CheckOpaque(q.Cursor); values.Add(new("continuationToken", q.Cursor)); }
        return values;
    }
    private static int PageSize(JsonElement root, int count, int? requested)
    {
        if (!root.TryGetProperty("pageSize", out var number) || !number.TryGetInt32(out var size)) throw new ManagementPortalResponseException();
        if (size is < 1 or > 100 || size != requested || count > size) throw new ManagementPortalResponseException();
        return size;
    }
    private static string? Continuation(JsonElement root)
    {
        if (!root.TryGetProperty("continuationToken", out var token) || token.ValueKind == JsonValueKind.Null) return null;
        if (token.ValueKind != JsonValueKind.String) throw new ManagementPortalResponseException();
        var value = token.GetString()!; CheckOpaque(value); return value;
    }
    internal static void CheckOpaque(string? id)
    {
        if (id is null || id.Length is < 4 or > 8192 || !Regex.IsMatch(id, "^d1_[A-Za-z0-9_-]+$")) throw new ManagementPortalResponseException();
    }
    internal static SystemErrorDto SafeError(SystemErrorDto row, bool allowUnsanitized = false)
    {
        if (row is null) throw new ManagementPortalResponseException();
        CheckOpaque(row.Id); if (row.OccurredAtUtc == default) throw new ManagementPortalResponseException();
        var source = row.Source == "RequestPipeline" ? "RequestPipeline" : "System";
        return new(row.Id, row.OccurredAtUtc.ToUniversalTime(), source,
            source == "RequestPipeline" ? "An unexpected request processing error was recorded." : "An unexpected system error was recorded.", SafeCorrelation(row.CorrelationId)) { Unsanitized = allowUnsanitized ? ValidateRaw(row.Unsanitized) : null };
    }
    internal static SystemLogDto SafeLog(SystemLogDto row, bool allowUnsanitized = false)
    {
        if (row is null) throw new ManagementPortalResponseException();
        CheckOpaque(row.Id); if (row.OccurredAtUtc == default) throw new ManagementPortalResponseException();
        var (source, summary) = SystemLogSources.Describe(row.Source ?? "", new());
        return new(row.Id, row.OccurredAtUtc.ToUniversalTime(), SystemLogLevels.All.Contains(row.Level) ? row.Level : "Unknown", source, summary, SafeCorrelation(row.CorrelationId)) { Unsanitized = allowUnsanitized ? ValidateRaw(row.Unsanitized) : null };
    }
    private static SystemErrorUnsanitized ValidateRaw(SystemErrorUnsanitized? raw)
    {
        if (raw is null) throw new ManagementPortalResponseException();
        CheckRaw(raw.Source, raw.TraceId, raw.Message, raw.Exception, raw.Path, raw.Method); return raw;
    }
    private static SystemLogUnsanitized ValidateRaw(SystemLogUnsanitized? raw)
    {
        if (raw is null) throw new ManagementPortalResponseException();
        CheckRaw(raw.Source, raw.Level, raw.TraceId, raw.Message, raw.Detail); return raw;
    }
    private static void CheckRaw(params string?[] fields)
    { if (fields.Any(x => x?.Length > 32768)) throw new ManagementPortalResponseException(); }
    private static string? SafeCorrelation(string? value) => value is not null && Regex.IsMatch(value, "^c1_[a-f0-9]{64}$") ? value : null;
}
