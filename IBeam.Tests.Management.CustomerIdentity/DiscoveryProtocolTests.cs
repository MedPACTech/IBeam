using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Services;
using IBeam.Management.Services.CustomerIdentity;
using Microsoft.Extensions.Options;
using Moq;
namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class DiscoveryProtocolTests
{
    internal static ManagementCustomerIdentityInfo Info => new("atlas", "Testing", Guid.Parse("bc819c23-496b-4a40-9c87-42e07def2203"),
        "dedicated-app", 1, [ManagementCustomerIdentityOperations.Info]);
    [TestMethod]
    [DataRow("version")] [DataRow("realm")] [DataRow("scope")] [DataRow("application")] [DataRow("environment")]
    [DataRow("unknown")] [DataRow("duplicate")] [DataRow("missing-info")]
    public void InvalidModuleMetadataIsRejected(string change)
    {
        var info = Info;
        info = change switch {
            "version" => info with { ProtocolVersion = 99 }, "realm" => info with { IdentityRealmId = Guid.Empty },
            "scope" => info with { RealmScope = "all-apps" }, "application" => info with { ApplicationKey = "" },
            "environment" => info with { Environment = "" }, "unknown" => info with { Capabilities = ["god-access"] },
            "duplicate" => info with { Capabilities = [ManagementCustomerIdentityOperations.Info, ManagementCustomerIdentityOperations.Info] },
            _ => info with { Capabilities = [ManagementCustomerIdentityOperations.TenantsRead] } };
        Assert.Throws<ManagementConnectionInvalidException>(() => ManagementCustomerIdentityProtocol.Validate(info));
    }
    [TestMethod]
    [DataRow("diagnostics-only")] [DataRow("identity-only")] [DataRow("combined")] [DataRow("unsupported-customer-version")]
    public async Task ModuleNegotiationPreservesIndependentCompatibility(string mode)
    {
        var handler = new DiscoveryHandler(path => path == "/api/management-diagnostics/info"
            ? mode == "identity-only" ? new(HttpStatusCode.NotFound) : Diagnostics()
            : mode == "diagnostics-only" ? new(HttpStatusCode.NotFound)
            : Customer(mode == "unsupported-customer-version" ? Info with { ProtocolVersion = 99 } : Info));
        var probe = Probe(handler);
        var profile = await probe.ProbeProfileAsync("https://atlas.example", new("fixture-private-machine-key"));
        Assert.AreEqual(mode != "identity-only", profile.Diagnostics is not null);
        Assert.AreEqual(mode switch { "diagnostics-only" => "not-installed", "unsupported-customer-version" => "incompatible", _ => "available" }, profile.CustomerIdentity.Status);
        Assert.HasCount(2, handler.Paths);
        Assert.IsTrue(handler.Keys.All(x => x == "fixture-private-machine-key"));
    }
    [TestMethod]
    [DataRow("invalid-diagnostics")] [DataRow("both-missing")] [DataRow("mismatched-binding")] [DataRow("identity-unsupported")]
    public async Task UnusableOrSubstitutedProfilesCannotActivateAConnection(string mode)
    {
        var handler = new DiscoveryHandler(path => path.Contains("management-diagnostics")
            ? mode == "invalid-diagnostics" ? Diagnostics(99) : mode == "mismatched-binding" ? Diagnostics() : new(HttpStatusCode.NotFound)
            : mode == "both-missing" ? new(HttpStatusCode.NotFound)
            : Customer(mode == "identity-unsupported" ? Info with { ProtocolVersion = 99 } : Info with { ApplicationKey = "other" }));
        await Assert.ThrowsAsync<ManagementConnectionInvalidException>(() => Probe(handler).ProbeProfileAsync("https://atlas.example", new("private-key")));
    }
    [TestMethod]
    [DataRow("malformed")] [DataRow("oversized")] [DataRow("provider-error")] [DataRow("redirect")]
    public async Task CustomerFailuresStaySafeAndDoNotRemoveValidDiagnostics(string mode)
    {
        var handler = new DiscoveryHandler(path => path.Contains("management-diagnostics") ? Diagnostics()
            : new(mode == "provider-error" ? HttpStatusCode.InternalServerError : mode == "redirect" ? HttpStatusCode.Redirect : HttpStatusCode.OK)
                { Content = new StringContent(mode == "oversized" ? new string('x', 40000) : "PRIVATE-CANARY-NOT-JSON") });
        var profile = await Probe(handler).ProbeProfileAsync("https://atlas.example", new("private-key"));
        Assert.IsNotNull(profile.Diagnostics);
        Assert.IsNull(profile.CustomerIdentity.Info);
        Assert.DoesNotContain("PRIVATE-CANARY", JsonSerializer.Serialize(profile));
        Assert.AreEqual(mode is "malformed" or "oversized" ? "incompatible" : "unavailable", profile.CustomerIdentity.Status);
    }
    [TestMethod]
    public async Task ArbitraryOriginsNeverReachTheNewDiscoveryTransport()
    {
        var handler = new DiscoveryHandler(_ => Customer(Info));
        await Assert.ThrowsAsync<ManagementConnectionInvalidException>(() => Probe(handler).ProbeProfileAsync("https://evil.example", new("private-key")));
        Assert.HasCount(0, handler.Paths);
    }
    [TestMethod]
    public async Task CallerCancellationRemainsCancellationWithoutProviderDetails()
    {
        var handler = new DiscoveryHandler(_ => Diagnostics()) { Block = true };
        using var cts = new CancellationTokenSource(100);
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => Probe(handler).ProbeProfileAsync("https://atlas.example", new("private-key"), cts.Token));
        Assert.DoesNotContain("private-key", error.ToString());
    }
    [TestMethod]
    public void PublicDtosDoNotExposeAuthenticationOrBusinessPayloadProperties()
    {
        var forbidden = new[] { "password", "hash", "securitystamp", "otp", "session", "token", "secret", "profile", "payload", "business" };
        foreach (var type in new[] { typeof(ManagementCustomerIdentityInfo), typeof(ManagementCustomerTenantDto), typeof(ManagementCustomerUserDto) })
            foreach (var property in type.GetProperties())
                Assert.IsFalse(forbidden.Any(x => property.Name.Contains(x, StringComparison.OrdinalIgnoreCase)), type.Name + "." + property.Name);
        Assert.IsFalse(typeof(ManagementCustomerTenantDto).Assembly.GetReferencedAssemblies().Any(x => x.Name!.StartsWith("IBeam.Identity")));
        Assert.IsFalse(typeof(ManagementCustomerIdentityAvailabilityService).Assembly.GetReferencedAssemblies().Any(x => x.Name!.StartsWith("IBeam.Identity")));
    }
    private static HttpResponseMessage Diagnostics(int version = 1) => new(HttpStatusCode.OK) {
        Content = JsonContent.Create(new { success = true, data = new ManagementDiagnosticsInfo("atlas", "Testing", version,
            [ManagementOperations.DiagnosticsInfo, ManagementOperations.ErrorsRead]) }) };
    private static HttpResponseMessage Customer(ManagementCustomerIdentityInfo info) => new(HttpStatusCode.OK) {
        Content = JsonContent.Create(new { success = true, data = info }) };
    internal static ManagementTargetConnectionProbe Probe(DiscoveryHandler handler)
    {
        var options = Options.Create(new ManagementConnectionOptions { ApprovedOrigins = ["https://atlas.example"], TimeoutSeconds = 2 });
        return new(new ManagementConnectionNetworkPolicy(options, Mock.Of<IManagementTargetDnsResolver>(), Mock.Of<IManagementTargetSocketConnector>()), options)
            { TestTransportFactory = () => handler };
    }
}
internal sealed class DiscoveryHandler(Func<string, HttpResponseMessage> response) : HttpMessageHandler
{
    public List<string> Paths { get; } = [];
    public List<string> Keys { get; } = [];
    public bool Block { get; init; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Paths.Add(request.RequestUri!.AbsolutePath);
        Keys.Add(request.Headers.GetValues("X-IBeam-Management-Key").Single());
        Assert.IsNull(request.Headers.Authorization);
        Assert.IsFalse(request.Headers.Contains("traceparent"));
        if (Block) await Task.Delay(Timeout.Infinite, ct);
        return response(request.RequestUri.AbsolutePath);
    }
}
