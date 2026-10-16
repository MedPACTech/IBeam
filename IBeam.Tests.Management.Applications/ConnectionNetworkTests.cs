using System.Net;
using System.Net.Http.Json;
using System.Text;
using IBeam.Management;
using IBeam.Management.Services;
using Microsoft.Extensions.Options;
namespace IBeam.Tests.Management.Applications;

[TestClass]
public sealed class ConnectionNetworkTests
{
    [TestMethod]
    [DataRow("127.0.0.1")] [DataRow("10.1.2.3")] [DataRow("169.254.169.254")] [DataRow("172.16.1.1")]
    [DataRow("192.168.1.1")] [DataRow("100.64.0.1")] [DataRow("0.0.0.0")] [DataRow("198.18.0.1")]
    [DataRow("192.0.2.1")] [DataRow("203.0.113.1")] [DataRow("224.0.0.1")] [DataRow("::1")]
    [DataRow("fe80::1")] [DataRow("fc00::1")] [DataRow("::ffff:127.0.0.1")] [DataRow("2001:db8::1")]
    [DataRow("2002:7f00:1::")] [DataRow("64:ff9b::7f00:1")] [DataRow("3fff::1")]
    public void PrivateSpecialAndTransitionAddressesAreDenied(string address)
        => Assert.IsFalse(ManagementConnectionNetworkPolicy.IsPublicAddress(IPAddress.Parse(address)));

    [TestMethod]
    public async Task DnsAnswersAreValidatedTogetherAndConnectionUsesPinnedIp()
    {
        var dns = new FixtureDns { Addresses = [IPAddress.Parse("8.8.8.8")] };
        var sockets = new FixtureSockets();
        var network = Network(dns, sockets);
        using var stream = await network.ConnectAsync(new("https://api.qurvia.app"), new("api.qurvia.app", 443), default);
        Assert.AreEqual(IPAddress.Parse("8.8.8.8"), sockets.Endpoint!.Address);
        Assert.AreEqual(1, dns.Calls);
        dns.Addresses = [IPAddress.Parse("8.8.8.8"), IPAddress.Loopback];
        await Assert.ThrowsAsync<ManagementConnectionInvalidException>(async () => await network.ConnectAsync(new("https://api.qurvia.app"), new("api.qurvia.app", 443), default));
        Assert.AreEqual(1, sockets.Calls);
        await Assert.ThrowsAsync<ManagementConnectionInvalidException>(async () => await network.ConnectAsync(new("https://api.qurvia.app"), new("evil.example", 443), default));
        await Assert.ThrowsAsync<ManagementConnectionInvalidException>(async () => await network.ConnectAsync(new("https://api.qurvia.app"), new("api.qurvia.app", 80), default));
        dns.Addresses = [];
        await Assert.ThrowsAsync<ManagementConnectionInvalidException>(async () => await network.ConnectAsync(new("https://api.qurvia.app"), new("api.qurvia.app", 443), default));
        Assert.IsTrue(ManagementConnectionNetworkPolicy.IsPublicAddress(IPAddress.Parse("2606:4700:4700::1111")));
    }
    [TestMethod]
    [DataRow("https://api.qurvia.app.evil.example")]
    [DataRow("https://api.qurvia.app@evil.example")]
    [DataRow("https://other.example")]
    [DataRow("https://127.0.0.1")]
    [DataRow("http://api.qurvia.app")]
    [DataRow("https://api.qurvia.app/other")]
    [DataRow("https://api.qurvia.app.")]
    public async Task ArbitraryOriginsAndHostSubstitutionsNeverReachTransport(string origin)
    {
        var handler = new FixtureHttp();
        var probe = Probe(handler);
        await Assert.ThrowsAsync<ManagementConnectionInvalidException>(() => probe.ProbeAsync(origin, new(ConnectionFixture.Key)));
        Assert.AreEqual(0, handler.Calls);
    }
    [TestMethod]
    public async Task TransportUsesFixedGetHeaderAndDoesNotForwardRedirectOrErrorBodies()
    {
        var handler = new FixtureHttp { Response = new(HttpStatusCode.Redirect) };
        handler.Response.Headers.Location = new("https://evil.example");
        handler.Response.Content = new StringContent(ConnectionFixture.Key);
        var probe = Probe(handler);
        var error = await Assert.ThrowsAsync<ManagementConnectionInvalidException>(() => probe.ProbeAsync("https://api.qurvia.app", new(ConnectionFixture.Key)));
        Assert.AreEqual(1, handler.Calls);
        Assert.AreEqual("/api/management-diagnostics/info", handler.Uri!.AbsolutePath);
        Assert.AreEqual("api.qurvia.app", handler.Uri.Host);
        Assert.AreEqual(ConnectionFixture.Key, handler.Key);
        Assert.IsNull(handler.HostHeader);
        Assert.IsFalse(handler.TraceHeader);
        Assert.DoesNotContain(ConnectionFixture.Key, error.ToString());
        using var production = probe.CreateHandler(new("https://api.qurvia.app"));
        Assert.IsFalse(production.AllowAutoRedirect); Assert.IsFalse(production.UseProxy); Assert.IsFalse(production.UseCookies);
        Assert.IsNull(production.ActivityHeadersPropagator); Assert.IsNotNull(production.ConnectCallback);
        Assert.IsNull(production.SslOptions.RemoteCertificateValidationCallback);
    }
    [TestMethod]
    public async Task ProbeParsesOnlyBoundedInfoAndSanitizesRawFailures()
    {
        var info = new ManagementDiagnosticsInfo("qurvia", "Production", 1, [ManagementOperations.DiagnosticsInfo]);
        var handler = new FixtureHttp { Response = new(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true, data = info }) } };
        Assert.AreEqual("qurvia", (await Probe(handler).ProbeAsync("https://api.qurvia.app", new(ConnectionFixture.Key))).ApplicationKey);
        foreach (var body in new[] { ConnectionFixture.Key, new string('x', 32769),
            "{\"success\":false,\"error\":\"" + ConnectionFixture.Key + "\"}" })
        {
            var malformed = new FixtureHttp { Response = new(HttpStatusCode.OK) { Content = new StringContent(body) } };
            var error = await Assert.ThrowsAsync<ManagementConnectionInvalidException>(() => Probe(malformed).ProbeAsync("https://api.qurvia.app", new(ConnectionFixture.Key)));
            Assert.DoesNotContain(ConnectionFixture.Key, error.ToString());
        }
        var outage = new FixtureHttp { Failure = new HttpRequestException(ConnectionFixture.Key) };
        var failure = await Assert.ThrowsAsync<ManagementConnectionUnavailableException>(() => Probe(outage).ProbeAsync("https://api.qurvia.app", new(ConnectionFixture.Key)));
        Assert.DoesNotContain(ConnectionFixture.Key, failure.ToString());
    }
    [TestMethod]
    public async Task ProbeTimeoutAndCancellationNeverReturnRawTransportErrors()
    {
        var slow = new FixtureHttp { Delay = true };
        await Assert.ThrowsAsync<ManagementConnectionTimeoutException>(() => Probe(slow).ProbeAsync("https://api.qurvia.app", new(ConnectionFixture.Key)));
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAsync<OperationCanceledException>(() => Probe(new FixtureHttp { Delay = true }).ProbeAsync("https://api.qurvia.app", new(ConnectionFixture.Key), cancel.Token));
    }
    [TestMethod, DataRow("Testing", true), DataRow("Production", false)]
    public async Task ProbePreservesAndValidatesSerializedRawCapabilities(string environment, bool permitted)
    {
        var info = new ManagementDiagnosticsInfo("qurvia", environment, 1, [ManagementOperations.DiagnosticsInfo, ManagementOperations.ErrorsRead]) { UnsanitizedCapabilities = [ManagementOperations.ErrorsRead] };
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true, data = info }) };
        if (permitted) Assert.AreEqual(ManagementOperations.ErrorsRead, (await ManagementTargetConnectionProbe.ReadInfoResponseAsync(response, default)).UnsanitizedCapabilities!.Single());
        else await Assert.ThrowsAsync<ManagementConnectionInvalidException>(() => ManagementTargetConnectionProbe.ReadInfoResponseAsync(response, default));
    }
    [TestMethod]
    public async Task ProbeRejectsMalformedRawModeMetadataWithoutEchoingIt()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true, data = new {
            applicationKey = "qurvia", environment = "Testing", protocolVersion = 1, capabilities = new[] { ManagementOperations.DiagnosticsInfo }, unsanitizedCapabilities = ConnectionFixture.Key } }) };
        var error = await Assert.ThrowsAsync<ManagementConnectionInvalidException>(() => ManagementTargetConnectionProbe.ReadInfoResponseAsync(response, default));
        Assert.DoesNotContain(ConnectionFixture.Key, error.ToString());
    }
    private static ManagementConnectionNetworkPolicy Network(FixtureDns dns, FixtureSockets sockets)
        => new(Options.Create(new ManagementConnectionOptions { ApprovedOrigins = ["https://api.qurvia.app"] }), dns, sockets);
    private static ManagementTargetConnectionProbe Probe(FixtureHttp handler)
        => new(Network(new(), new()), Options.Create(new ManagementConnectionOptions { TimeoutSeconds = 1 })) { TestTransportFactory = () => handler };

    private sealed class FixtureDns : IManagementTargetDnsResolver
    {
        public IPAddress[] Addresses { get; set; } = [];
        public int Calls { get; set; }
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) { Calls++; return Task.FromResult(Addresses); }
    }
    private sealed class FixtureSockets : IManagementTargetSocketConnector
    {
        public IPEndPoint? Endpoint { get; set; }
        public int Calls { get; set; }
        public ValueTask<Stream> ConnectAsync(IPEndPoint endpoint, CancellationToken ct)
        { Calls++; Endpoint = endpoint; return ValueTask.FromResult<Stream>(new MemoryStream()); }
    }
    private sealed class FixtureHttp : HttpMessageHandler
    {
        public HttpResponseMessage Response { get; set; } = new(HttpStatusCode.ServiceUnavailable);
        public Exception? Failure { get; set; }
        public bool Delay { get; set; }
        public int Calls { get; set; }
        public Uri? Uri { get; set; }
        public string? Key { get; set; }
        public string? HostHeader { get; set; }
        public bool TraceHeader { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; Uri = request.RequestUri; Key = request.Headers.GetValues("X-IBeam-Management-Key").Single();
            HostHeader = request.Headers.Host; TraceHeader = request.Headers.Contains("traceparent");
            Assert.AreEqual(HttpMethod.Get, request.Method); Assert.IsNull(request.Content);
            if (Delay) await Task.Delay(Timeout.Infinite, ct);
            if (Failure is not null) throw Failure;
            return Response;
        }
    }
}
