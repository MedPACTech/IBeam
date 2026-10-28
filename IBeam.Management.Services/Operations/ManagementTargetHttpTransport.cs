using System.Net;

namespace IBeam.Management.Services;

/// <summary>Internal transport primitive shared by compiled built-ins, discovery and registered typed operations.
/// It is deliberately not a public URL/method proxy. Protocol callers retain typed validation and error mapping.</summary>
internal static class ManagementTargetHttpTransport
{
    internal static HttpClient CreateClient(ManagementConnectionNetworkPolicy network, Uri approved,
        Func<HttpMessageHandler>? testTransport = null, int connectTimeoutSeconds = 5)
    {
        _ = network.RequireOrigin(approved.GetLeftPart(UriPartial.Authority));
        return new(testTransport?.Invoke() ?? CreateHandler(network, approved, connectTimeoutSeconds), disposeHandler: true)
            { Timeout = Timeout.InfiniteTimeSpan };
    }
    internal static SocketsHttpHandler CreateHandler(ManagementConnectionNetworkPolicy network, Uri approved, int connectTimeoutSeconds = 5)
    {
        if (connectTimeoutSeconds is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(connectTimeoutSeconds));
        return new() {
            AllowAutoRedirect = false, UseProxy = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.None,
            ActivityHeadersPropagator = null, ConnectTimeout = TimeSpan.FromSeconds(connectTimeoutSeconds),
            ConnectCallback = (context, token) => network.ConnectAsync(approved, context.DnsEndPoint, token)
        };
    }
}
