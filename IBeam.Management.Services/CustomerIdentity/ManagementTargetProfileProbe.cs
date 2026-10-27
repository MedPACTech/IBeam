using System.Net;
using System.Text.Json;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Services.CustomerIdentity;
namespace IBeam.Management.Services;

/// <summary>Opt-in module discovery. Diagnostics v1 parsing/sanitization is unchanged; customer negotiation is independent.</summary>
public sealed partial class ManagementTargetConnectionProbe : IManagementTargetProfileProbe
{
    public async Task<ManagementTargetProfile> ProbeProfileAsync(string origin, ManagementSecretValue key, CancellationToken ct = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        try
        {
            var approved = network.RequireOrigin(origin);
            using var handler = TestTransportFactory?.Invoke() ?? CreateHandler(approved);
            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            async Task<HttpResponseMessage> Send(string path)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(approved, path))
                    { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
                request.Headers.Add("X-IBeam-Management-Key", key.Value);
                return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            }
            ManagementDiagnosticsInfo? diagnostics = null;
            using (var response = await Send("/api/management-diagnostics/info"))
            {
                if (response.StatusCode != HttpStatusCode.NotFound)
                    diagnostics = await ReadInfoResponseAsync(response, deadline.Token);
            }
            ManagementCustomerIdentityDiscovery customer;
            using (var response = await Send("/api/management-identity/info"))
            {
                if (response.StatusCode == HttpStatusCode.NotFound) customer = new(ManagementCustomerIdentityStatuses.NotInstalled);
                else if (response.StatusCode != HttpStatusCode.OK) customer = new(ManagementCustomerIdentityStatuses.Unavailable);
                else customer = await ReadCustomerInfoResponseAsync(response, deadline.Token);
            }
            var result = new ManagementTargetProfile(diagnostics, customer);
            var app = diagnostics?.ApplicationKey ?? customer.Info?.ApplicationKey ?? "";
            var environment = diagnostics?.Environment ?? customer.Info?.Environment ?? "";
            ManagementCustomerIdentityProtocol.ValidateProfile(result, app, environment);
            return result;
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
    internal static async Task<ManagementCustomerIdentityDiscovery> ReadCustomerInfoResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode != HttpStatusCode.OK) return new(ManagementCustomerIdentityStatuses.Unavailable);
        if (response.Content.Headers.ContentLength > 32768) return new(ManagementCustomerIdentityStatuses.Incompatible);
        try
        {
            using var input = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var bytes = new byte[4096];
            while (true)
            {
                var read = await input.ReadAsync(bytes.AsMemory(0, (int)Math.Min(bytes.Length, 32769 - buffer.Length)), ct);
                if (read == 0) break;
                buffer.Write(bytes, 0, read);
                if (buffer.Length > 32768) return new(ManagementCustomerIdentityStatuses.Incompatible);
            }
            using var json = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
            var root = json.RootElement;
            if (!root.GetProperty("success").GetBoolean()) return new(ManagementCustomerIdentityStatuses.Incompatible);
            var d = root.GetProperty("data");
            var info = new ManagementCustomerIdentityInfo(d.GetProperty("applicationKey").GetString()!,
                d.GetProperty("environment").GetString()!, d.GetProperty("identityRealmId").GetGuid(),
                d.GetProperty("realmScope").GetString()!, d.GetProperty("protocolVersion").GetInt32(),
                Array.AsReadOnly(d.GetProperty("capabilities").EnumerateArray().Select(x => x.GetString()!).ToArray())) {
                    OperationReceiptProtocolVersion = d.TryGetProperty("operationReceiptProtocolVersion", out var receipts) ? receipts.GetInt32() : 0,
                    OwnershipProtectionProtocolVersion = d.TryGetProperty("ownershipProtectionProtocolVersion", out var ownership) ? ownership.GetInt32() : 0,
                    OwnershipTransferProtocolVersion = d.TryGetProperty("ownershipTransferProtocolVersion", out var transfer) ? transfer.GetInt32() : 0 };
            ManagementCustomerIdentityProtocol.Validate(info);
            return new(ManagementCustomerIdentityStatuses.Available, info);
        }
        catch (OperationCanceledException) { throw; }
        catch { return new(ManagementCustomerIdentityStatuses.Incompatible); }
    }
}
