using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
namespace IBeam.Management.Services;

public sealed class ManagementConnectionOptions
{
    public const string SectionName = "IBeam:Management:Connections";
    public string[] ApprovedOrigins { get; set; } = [];
    public int TimeoutSeconds { get; set; } = 10;
    /// <summary>Host-owned opt-in; old diagnostic/Identity probes keep their two-request behavior.</summary>
    public bool EnableRegisteredOperationsDiscovery { get; set; }
}
public interface IManagementTargetDnsResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct);
}
public interface IManagementTargetSocketConnector
{
    ValueTask<Stream> ConnectAsync(IPEndPoint endpoint, CancellationToken ct);
}
public sealed class ManagementTargetDnsResolver : IManagementTargetDnsResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) => Dns.GetHostAddressesAsync(host, ct);
}
public sealed class ManagementTargetSocketConnector : IManagementTargetSocketConnector
{
    public async ValueTask<Stream> ConnectAsync(IPEndPoint endpoint, CancellationToken ct)
    {
        var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try { await socket.ConnectAsync(endpoint, ct); return new NetworkStream(socket, ownsSocket: true); }
        catch { socket.Dispose(); throw; }
    }
}
/// <summary>Exact approved origin, public-only DNS answers and IP-pinned sockets. TLS still validates the original URL/SNI.</summary>
public sealed class ManagementConnectionNetworkPolicy(IOptions<ManagementConnectionOptions> options,
    IManagementTargetDnsResolver dns, IManagementTargetSocketConnector sockets)
{
    public Uri RequireOrigin(string origin)
    {
        var canonical = ManagementApplicationService.NormalizeOrigin(origin);
        if (!options.Value.ApprovedOrigins.Any(x => ManagementApplicationService.NormalizeOrigin(x) == canonical))
            throw new ManagementConnectionInvalidException();
        return new Uri(canonical);
    }
    public async ValueTask<Stream> ConnectAsync(Uri approvedOrigin, DnsEndPoint requestedEndpoint, CancellationToken ct)
    {
        var expected = RequireOrigin(approvedOrigin.GetLeftPart(UriPartial.Authority));
        if (!string.Equals(requestedEndpoint.Host, expected.IdnHost, StringComparison.OrdinalIgnoreCase) || requestedEndpoint.Port != 443)
            throw new ManagementConnectionInvalidException();
        var addresses = await dns.ResolveAsync(expected.IdnHost, ct);
        if (addresses.Length is 0 or > 16 || addresses.Any(x => !IsPublicAddress(x))) throw new ManagementConnectionInvalidException();
        // Never pass a hostname to the connector or re-resolve between checking and connecting.
        return await sockets.ConnectAsync(new IPEndPoint(addresses[0], 443), ct);
    }
    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) return false;
        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return !(b[0] is 0 or 10 or 127 || b[0] >= 224 ||
                b[0] == 100 && b[1] is >= 64 and <= 127 ||
                b[0] == 169 && b[1] == 254 ||
                b[0] == 172 && b[1] is >= 16 and <= 31 ||
                b[0] == 192 && (b[1] == 168 || b[1] == 0 && b[2] is 0 or 2 || b[1] == 88 && b[2] == 99) ||
                b[0] == 198 && (b[1] is 18 or 19 || b[1] == 51 && b[2] == 100) ||
                b[0] == 203 && b[1] == 0 && b[2] == 113);
        }
        // Conservative globally-routable IPv6 allow range; exclude special, documentation and transition ranges.
        return address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId == 0 &&
            (b[0] & 0xe0) == 0x20 &&
            !(b[0] == 0x20 && b[1] == 1 && b[2] < 2) &&
            !(b[0] == 0x20 && b[1] == 1 && b[2] == 0x0d && b[3] == 0xb8) &&
            !(b[0] == 0x20 && b[1] == 2) &&
            !(b[0] == 0x3f && b[1] == 0xff && (b[2] & 0xf0) == 0);
    }
}
