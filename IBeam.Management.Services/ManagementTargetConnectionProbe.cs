using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Services;

/// <summary>Dedicated HTTP transport without factory logging, redirects, proxies, cookies or propagated trace headers.</summary>
public sealed class ManagementTargetConnectionProbe(ManagementConnectionNetworkPolicy network,
    IOptions<ManagementConnectionOptions> options) : IManagementTargetConnectionProbe
{
    public async Task<ManagementDiagnosticsInfo> ProbeAsync(string origin, ManagementSecretValue key, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        try
        {
            var approved = network.RequireOrigin(origin);
            using var handler = TestTransportFactory?.Invoke() ?? CreateHandler(approved);
            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(approved, "/api/management-diagnostics/info"))
                { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
            request.Headers.Add("X-IBeam-Management-Key", key.Value);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return await ReadInfoResponseAsync(response, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (ct.IsCancellationRequested) throw new OperationCanceledException("The management connection was cancelled.", ct);
            throw new ManagementConnectionTimeoutException();
        }
        catch (ManagementApplicationInvalidException) { throw new ManagementConnectionInvalidException(); }
        catch (ManagementConnectionInvalidException) { throw; }
        catch { throw new ManagementConnectionUnavailableException(); }
    }
    internal Func<HttpMessageHandler>? TestTransportFactory { get; init; }
    internal SocketsHttpHandler CreateHandler(Uri approved) => new SocketsHttpHandler
            {
                AllowAutoRedirect = false, UseProxy = false, UseCookies = false,
                AutomaticDecompression = DecompressionMethods.None, ActivityHeadersPropagator = null,
                ConnectTimeout = TimeSpan.FromSeconds(Math.Min(5, options.Value.TimeoutSeconds)),
                ConnectCallback = (context, token) => network.ConnectAsync(approved, context.DnsEndPoint, token)
            };

    // Shared bounded parser for protocol compatibility tests; no target response text is ever returned in failures.
    internal static async Task<ManagementDiagnosticsInfo> ReadInfoResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength > 32768)
            throw new ManagementConnectionInvalidException();
        using var input = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        while (true)
        {
            var read = await input.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, 32769 - buffer.Length)), ct);
            if (read == 0) break;
            buffer.Write(chunk, 0, read);
            if (buffer.Length > 32768) throw new ManagementConnectionInvalidException();
        }
        try
        {
            using var json = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
            var root = json.RootElement;
            if (!root.GetProperty("success").GetBoolean()) throw new ManagementConnectionInvalidException();
            var data = root.GetProperty("data");
            var info = new ManagementDiagnosticsInfo(data.GetProperty("applicationKey").GetString()!,
                data.GetProperty("environment").GetString()!, data.GetProperty("protocolVersion").GetInt32(),
                data.GetProperty("capabilities").EnumerateArray().Select(x => x.GetString()!).ToArray());
            if (data.TryGetProperty("unsanitizedCapabilities", out var raw) && raw.ValueKind != JsonValueKind.Null)
                info = info with { UnsanitizedCapabilities = raw.EnumerateArray().Select(x => x.GetString()!).ToArray() };
            ManagementConnectionProtocol.Validate(info);
            return info;
        }
        catch (ManagementConnectionInvalidException) { throw; }
        catch { throw new ManagementConnectionInvalidException(); }
    }
}
public static class ManagementConnectionProtocol
{
    public static void Validate(ManagementDiagnosticsInfo info)
    {
        var allowed = new[] { ManagementOperations.DiagnosticsInfo, ManagementOperations.ErrorsRead, ManagementOperations.LogsRead };
        if (info is null || info.ProtocolVersion != 1 || string.IsNullOrWhiteSpace(info.ApplicationKey) ||
            info.ApplicationKey.Length > 100 || string.IsNullOrWhiteSpace(info.Environment) || info.Environment.Length > 100 ||
            info.Capabilities is null || info.Capabilities.Count is < 1 or > 3 ||
            info.Capabilities.Distinct(StringComparer.Ordinal).Count() != info.Capabilities.Count ||
            !info.Capabilities.Contains(ManagementOperations.DiagnosticsInfo, StringComparer.Ordinal) ||
            info.Capabilities.Any(x => !allowed.Contains(x, StringComparer.Ordinal)))
            throw new ManagementConnectionInvalidException();
        var raw = info.UnsanitizedCapabilities;
        if (raw is not null && (raw.Count > 2 || raw.Distinct(StringComparer.Ordinal).Count() != raw.Count ||
            raw.Any(x => x is not (ManagementOperations.ErrorsRead or ManagementOperations.LogsRead) || !info.Capabilities.Contains(x, StringComparer.Ordinal)) ||
            raw.Count > 0 && !Diagnostics.DiagnosticSanitizationPolicy.NonProductionDeployment(info.Environment)))
            throw new ManagementConnectionInvalidException();
    }
}
