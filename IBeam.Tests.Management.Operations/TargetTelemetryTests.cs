using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Licensing;
using IBeam.Management.Models;
using IBeam.Management.Operations;
using IBeam.Management.Services;
using IBeam.Management.Services.Audit;
using IBeam.Management.Services.CustomerIdentity;
using IBeam.Management.Services.Diagnostics;
using Microsoft.Extensions.Options;
using Moq;

namespace IBeam.Tests.Management.Operations;

public sealed partial class RegisteredTransportTests
{
    [TestMethod]
    public async Task TargetTelemetryCustomSuccessHasBoundedScopeDurationAndNoAmbientBaggage()
    {
        await using var f = await Fixture.Create(includeSynthetic: true); using var capture = new TelemetryCapture();
        using var ambient = new Activity("PRIVATE-ACTIVITY").AddBaggage("private", "PRIVATE-PROFILE").Start();
        await f.Client.ExecuteAsync<SharedSenderSyntheticRequest, SharedSenderSyntheticResult>(f.AppId, SharedSenderSyntheticOperation, f.Tenant, f.License, new(), "", null);
        var record = capture.For(f.AppId).Single(); Assert.AreEqual("success", record.Outcome); Assert.AreEqual("none", record.FailureClass);
        Assert.AreEqual(f.Tenant, record.TenantId); Assert.AreEqual(f.License, record.ResourceId); Assert.AreEqual(SharedSenderSyntheticOperation, record.Operation);
        Assert.IsTrue(record.DurationMilliseconds is >= 0 and <= 86400000); Assert.AreEqual(200, record.HttpStatus); Assert.IsFalse(record.MutatingRequestSent);
        Assert.IsTrue(Guid.TryParseExact(record.CorrelationId, "N", out _)); capture.AssertPrivateAbsent(f.Secret);
    }
    [TestMethod]
    [DataRow("assignment")] [DataRow("invalid-type")] [DataRow("unknown-operation")] [DataRow("null-operation")]
    public async Task TargetTelemetryCapturesPreflightRejectionWithoutRawCallerMetadata(string cause)
    {
        await using var f = await Fixture.Create(includeSynthetic: true); using var capture = new TelemetryCapture();
        if (cause == "assignment") { f.Assignment = f.Assignment with { RevokedUtc = DateTimeOffset.UtcNow }; await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Read()); }
        else if (cause == "invalid-type") await Assert.ThrowsAsync<ManagementOperationInvalidException>(() => f.Client.ExecuteAsync<ManagementLicenseReadRequest, SharedSenderSyntheticResult>(f.AppId, SharedSenderSyntheticOperation, f.Tenant, f.License, new(), "", null));
        else await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Client.ExecuteAsync<SharedSenderSyntheticRequest, SharedSenderSyntheticResult>(f.AppId, cause == "null-operation" ? null! : "PRIVATE-CALLER-OPERATION", f.Tenant, f.License, new(), "", null));
        var record = capture.For(f.AppId).Single(); Assert.AreEqual("rejected", record.Outcome); Assert.IsNull(record.HttpStatus); Assert.IsFalse(record.MutatingRequestSent);
        if (cause is "unknown-operation" or "null-operation") Assert.AreEqual("unregistered", record.Operation);
        Assert.AreEqual(0, f.SecretReads); capture.AssertPrivateAbsent(f.Secret);
    }
    [TestMethod]
    [DataRow("network")] [DataRow("timeout")] [DataRow("cancelled")]
    public async Task TargetTelemetrySeparatesNetworkDeadlineAndCallerCancellation(string cause)
    {
        await using var f = await Fixture.Create(timeoutSeconds: 1); using var capture = new TelemetryCapture(); using var caller = new CancellationTokenSource();
        if (cause == "network") f.BeforeSend = request => { if (!request.RequestUri!.AbsolutePath.EndsWith("/info", StringComparison.Ordinal)) throw new HttpRequestException("PRIVATE-NETWORK-PROVIDER"); };
        else f.TransportGate = (request, ct) => request.RequestUri!.AbsolutePath.EndsWith("/info", StringComparison.Ordinal) ? Task.CompletedTask : Task.Delay(Timeout.Infinite, ct);
        if (cause == "cancelled") caller.CancelAfter(50);
        Task<ManagementLicenseStatus> Call() => f.Client.ExecuteAsync<ManagementLicenseReadRequest, ManagementLicenseStatus>(f.AppId, ManagementLicenseOperations.Read, f.Tenant, f.License, new(), "", null, caller.Token);
        if (cause == "cancelled") await Assert.ThrowsAsync<OperationCanceledException>(Call); else await Assert.ThrowsAsync<ManagementOperationUnavailableException>(Call);
        var record = capture.For(f.AppId).Single(); Assert.AreEqual(cause == "network" ? "network-failure" : cause, record.Outcome);
        Assert.AreEqual(cause, record.FailureClass); Assert.AreEqual(0, f.WriteCount); capture.AssertPrivateAbsent(f.Secret);
    }
    [TestMethod]
    public async Task TargetTelemetryUnknownWriteRetainsOriginalIdCorrelationAndNoProviderText()
    {
        await using var f = await Fixture.Create(); var initial = await f.Read(); using var capture = new TelemetryCapture();
        f.AfterSend = (request, response) => { if (request.Method == HttpMethod.Post) { response.Dispose(); throw new HttpRequestException("PRIVATE-LOST-COMMIT-REPLY"); } return Task.FromResult(response); };
        var error = await Assert.ThrowsAsync<ManagementOperationUncertainException>(() => f.Suspend(initial.Version));
        var record = capture.For(f.AppId).Single(); Assert.AreEqual("unknown", record.Outcome); Assert.AreEqual("network", record.FailureClass);
        Assert.AreEqual(error.OperationId, record.OperationId); Assert.IsTrue(record.MutatingRequestSent); Assert.AreEqual(1, f.WriteCount);
        var proof = new JwtSecurityTokenHandler().ReadJwtToken(f.LastWrite!.Assertion);
        Assert.AreEqual(proof.Claims.Single(x => x.Type == "correlation").Value, record.CorrelationId);
        capture.AssertPrivateAbsent(f.Secret, f.LastWrite.Assertion, f.LastWrite.Body);
    }
    [TestMethod]
    public async Task TargetTelemetryObserverAndMeterFailuresCannotChangeSuccessDenialOrRepeatWrites()
    {
        await using var f = await Fixture.Create(); using var capture = new TelemetryCapture { Throw = true };
        using var meter = new MeterListener(); meter.InstrumentPublished = (instrument, listener) => { if (instrument.Meter.Name == ManagementTargetTelemetry.SourceName) listener.EnableMeasurementEvents(instrument); };
        meter.SetMeasurementEventCallback<long>((_, _, _, _) => throw new IOException("PRIVATE-METER-FAILURE")); meter.Start();
        var initial = await f.Read(); Assert.AreEqual("suspended", (await f.Suspend(initial.Version)).Status); Assert.AreEqual(1, f.WriteCount);
        f.Assignment = f.Assignment with { RevokedUtc = DateTimeOffset.UtcNow };
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Suspend(initial.Version)); Assert.AreEqual(1, f.WriteCount);
        Assert.AreEqual(3, capture.For(f.AppId).Count()); capture.AssertPrivateAbsent(f.Secret);
    }
    [TestMethod]
    public async Task TargetTelemetryMetricDimensionsAreBoundedNamesWithoutScopedIdsOrPayloads()
    {
        await using var f = await Fixture.Create(includeSynthetic: true); var tags = new List<string>();
        using var meter = new MeterListener(); meter.InstrumentPublished = (instrument, listener) => { if (instrument.Meter.Name == ManagementTargetTelemetry.SourceName) listener.EnableMeasurementEvents(instrument); };
        meter.SetMeasurementEventCallback<long>((_, _, values, _) => { foreach (var value in values) tags.Add(value.Key + "=" + value.Value); }); meter.Start();
        await f.Client.ExecuteAsync<SharedSenderSyntheticRequest, SharedSenderSyntheticResult>(f.AppId, SharedSenderSyntheticOperation, f.Tenant, f.License, new(), "", null);
        Assert.IsTrue(tags.Count > 0); Assert.IsTrue(tags.All(x => x.StartsWith("operation=") || x.StartsWith("channel=") || x.StartsWith("outcome=")));
        Assert.DoesNotContain(f.AppId.ToString("D"), string.Join(',', tags)); Assert.DoesNotContain(f.Secret, string.Join(',', tags));
    }
    [TestMethod]
    public async Task TargetTelemetryNativeIdentitySharesVerifiedCorrelationWithoutProfileOrAssertion()
    {
        var app = Guid.NewGuid(); var tenant = Guid.NewGuid(); var realm = Guid.NewGuid(); var audit = new ManagementAuditContext(); using var capture = new TelemetryCapture();
        var actors = new Mock<IManagementDelegatedActorProvider>(); actors.Setup(x => x.IssueAsync(app, It.IsAny<string>(), It.IsAny<ManagementDelegationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ManagementActorAssertion { Value = "PRIVATE-ACTOR-ASSERTION", ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(30) });
        var client = new ManagementCustomerIdentityClient(TelemetryNetwork(), Options.Create(new ManagementPortalCustomerDirectoryOptions()), actors.Object, audit) {
            TestTransportFactory = () => new TelemetryHttp(request => request.RequestUri!.AbsolutePath.EndsWith("/info", StringComparison.Ordinal)
                ? JsonContent.Create(new { success = true, data = new ManagementCustomerIdentityInfo("fixture", "Testing", realm, "dedicated-app", 1, [ManagementCustomerIdentityOperations.Info, ManagementCustomerIdentityOperations.TenantsRead]) })
                : JsonContent.Create(new { success = true, data = new ManagementCustomerTenantDto(tenant, "PRIVATE-CUSTOMER-PROFILE", "Active", null, null, "version") })) };
        var result = await client.FindAsync<ManagementCustomerTenantDto>(new() { ApplicationId = app, IdentityRealmId = realm, ApplicationKey = "fixture", Environment = "Testing", BaseUrl = "https://fixture.example", Key = new("PRIVATE-KEY") }, null, tenant);
        Assert.AreEqual("PRIVATE-CUSTOMER-PROFILE", result.Name); var records = capture.For(app).ToArray(); Assert.HasCount(2, records);
        Assert.IsTrue(records.All(x => x.CorrelationId == audit.CorrelationId && x.Channel == "identity")); Assert.AreEqual(tenant, records.Single(x => x.Kind == "read").TenantId);
        capture.AssertPrivateAbsent();
    }
    [TestMethod]
    public async Task TargetTelemetryDiscoveryDoesNotExposeOriginOrConnectionSecret()
    {
        using var capture = new TelemetryCapture(); var probe = new ManagementTargetConnectionProbe(TelemetryNetwork(), Options.Create(new ManagementConnectionOptions())) {
            TestTransportFactory = () => new TelemetryHttp(_ => JsonContent.Create(new { success = true, data = new ManagementDiagnosticsInfo("fixture", "Testing", 1, [ManagementOperations.DiagnosticsInfo]) })) };
        await probe.ProbeAsync("https://fixture.example", new("PRIVATE-DISCOVERY-KEY")); var record = capture.Records.Single();
        Assert.AreEqual("discovery", record.Channel); Assert.IsNull(record.ApplicationId); Assert.AreEqual("success", record.Outcome); capture.AssertPrivateAbsent("fixture.example");
    }
    private static ManagementConnectionNetworkPolicy TelemetryNetwork() => new(Options.Create(new ManagementConnectionOptions { ApprovedOrigins = ["https://fixture.example"] }), Mock.Of<IManagementTargetDnsResolver>(), Mock.Of<IManagementTargetSocketConnector>());
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task TargetTelemetryDiagnosticModeNeverWidensTelemetryRedaction(bool unsanitized)
    {
        using var capture = new TelemetryCapture(); var app = Guid.NewGuid(); var audit = new ManagementAuditContext();
        var row = new SystemErrorDto("d1_abcd", DateTimeOffset.UtcNow, "System", "PRIVATE-SUMMARY", null) {
            Unsanitized = new("PRIVATE-SOURCE", "PRIVATE-TRACE", "PRIVATE-MESSAGE", "PRIVATE-EXCEPTION", "PRIVATE-PATH", "GET") };
        var client = new ApplicationDiagnosticsClient(TelemetryNetwork(), Options.Create(new PortalDiagnosticsOptions()), audit) {
            TestTransportFactory = () => new TelemetryHttp(_ => JsonContent.Create(new { success = true, data = row })) };
        var result = await client.ErrorAsync(new() { ApplicationId = app, ApplicationKey = "fixture", Environment = "Testing", BaseUrl = "https://fixture.example", Key = new("PRIVATE-KEY"), AllowUnsanitized = unsanitized }, "d1_abcd");
        Assert.AreEqual(unsanitized, result.Unsanitized is not null); var record = capture.For(app).Single();
        Assert.AreEqual(ManagementOperations.ErrorsRead, record.Operation); Assert.AreEqual(audit.CorrelationId, record.CorrelationId); capture.AssertPrivateAbsent();
    }
    [TestMethod]
    public async Task TargetTelemetryMalformedCorrelationCannotBecomeAFreeTextField()
    {
        using var capture = new TelemetryCapture(); var app = Guid.NewGuid(); var audit = new Mock<IManagementAuditContext>(); audit.SetupGet(x => x.CorrelationId).Returns("PRIVATE-CORRELATION");
        var client = new ApplicationDiagnosticsClient(TelemetryNetwork(), Options.Create(new PortalDiagnosticsOptions()), audit.Object) {
            TestTransportFactory = () => new TelemetryHttp(_ => JsonContent.Create(new { success = true, data = new ManagementDiagnosticsInfo("fixture", "Testing", 1, [ManagementOperations.DiagnosticsInfo]) })) };
        await client.InfoAsync(new() { ApplicationId = app, ApplicationKey = "fixture", Environment = "Testing", BaseUrl = "https://fixture.example", Key = new("PRIVATE-KEY") });
        Assert.IsTrue(Guid.TryParseExact(capture.For(app).Single().CorrelationId, "N", out _)); capture.AssertPrivateAbsent();
    }
    [TestMethod]
    public async Task TargetTelemetryCustomMalformedResponseCannotExportCustomerFields()
    {
        await using var f = await Fixture.Create(includeSynthetic: true); using var capture = new TelemetryCapture();
        f.AfterSend = (request, response) => { if (request.RequestUri!.AbsolutePath.EndsWith("/info", StringComparison.Ordinal)) return Task.FromResult(response);
            response.Dispose(); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true, data = new { tenantId = f.Tenant, resourceId = f.License, count = 12, profile = "PRIVATE-CLINICAL-PROFILE" } }) }); };
        await Assert.ThrowsAsync<ManagementOperationUnavailableException>(() => f.Client.ExecuteAsync<SharedSenderSyntheticRequest, SharedSenderSyntheticResult>(f.AppId, SharedSenderSyntheticOperation, f.Tenant, f.License, new(), "", null));
        Assert.AreEqual("unavailable", capture.For(f.AppId).Single().Outcome); capture.AssertPrivateAbsent(f.Secret);
    }
    private sealed class TelemetryHttp(Func<HttpRequestMessage, HttpContent> body) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = body(request) }); }
    [TestMethod]
    public async Task TargetTelemetryRegisteredCustomAuditUsesVerifiedScopeAndNoGenericSnapshots()
    {
        await using var f = await Fixture.Create(includeSynthetic: true); using var capture = new TelemetryCapture();
        await f.Client.ExecuteAsync<SharedSenderSyntheticRequest, SharedSenderSyntheticResult>(f.AppId, SharedSenderSyntheticOperation, f.Tenant, f.License, new(), "", null);
        var entry = f.Audits.Single(x => x.Operation == SharedSenderSyntheticOperation);
        Assert.AreEqual(f.Org, entry.OrganizationId); Assert.AreEqual(f.AppId, entry.ApplicationId); Assert.AreEqual(f.Human, entry.ActorUserId);
        Assert.AreEqual(f.License, entry.SubjectId); Assert.AreEqual(capture.For(f.AppId).Single().CorrelationId, entry.CorrelationId);
        Assert.DoesNotContain(f.Secret, JsonSerializer.Serialize(f.Audits)); Assert.DoesNotContain("PRIVATE-", JsonSerializer.Serialize(f.Audits));
    }
    [TestMethod]
    public async Task TargetTelemetryDiscoveryAndInstrumentPublicationFailuresCannotPoisonInitialization()
    {
        await using var f = await Fixture.Create();
        using (DiagnosticListener.AllListeners.Subscribe(new FailingTelemetryDiscovery()))
            Assert.IsNull(ManagementTargetTelemetry.CreateSource());
        using (var listener = new MeterListener()) {
            listener.Start(); listener.InstrumentPublished = (instrument, _) => { if (instrument.Meter.Name == ManagementTargetTelemetry.SourceName) throw new IOException("PRIVATE-PUBLICATION-FAILURE"); };
            using var meter = ManagementTargetTelemetry.CreateMeter();
            Assert.IsNull(ManagementTargetTelemetry.CreateCalls(meter)); Assert.IsNull(ManagementTargetTelemetry.CreateDuration(meter));
        }
        Assert.AreEqual("active", (await f.Read()).Status);
    }
    private sealed class FailingTelemetryDiscovery : IObserver<DiagnosticListener>
    {
        private bool initialized;
        public void OnNext(DiagnosticListener source) { if (source.Name == ManagementTargetTelemetry.SourceName) { if (initialized) throw new IOException("PRIVATE-DISCOVERY-FAILURE"); initialized = true; } }
        public void OnError(Exception error) { } public void OnCompleted() { }
    }
    private sealed class TelemetryCapture : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        public ConcurrentBag<ManagementTargetCallTelemetry> Records { get; } = []; public bool Throw;
        private readonly List<IDisposable> subscriptions = [];
        public TelemetryCapture() => subscriptions.Add(DiagnosticListener.AllListeners.Subscribe(this));
        public IEnumerable<ManagementTargetCallTelemetry> For(Guid app) => Records.Where(x => x.ApplicationId == app);
        public void OnNext(DiagnosticListener listener) { if (listener.Name == ManagementTargetTelemetry.SourceName) subscriptions.Add(listener.Subscribe(this, name => name == ManagementTargetTelemetry.CompletionEvent)); }
        public void OnNext(KeyValuePair<string, object?> value) { if (value.Value is ManagementTargetCallTelemetry record) Records.Add(record); if (Throw) throw new IOException("PRIVATE-TRACER-FAILURE"); }
        public void OnError(Exception error) { } public void OnCompleted() { }
        public void AssertPrivateAbsent(params string[] extras) { var json = JsonSerializer.Serialize(Records); Assert.DoesNotContain("PRIVATE-", json); foreach (var extra in extras) Assert.DoesNotContain(extra, json); }
        public void Dispose() { foreach (var subscription in subscriptions) subscription.Dispose(); }
    }
}
