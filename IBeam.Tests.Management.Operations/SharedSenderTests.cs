using System.Net;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Licensing;
using IBeam.Management.Operations;
using IBeam.Management.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace IBeam.Tests.Management.Operations;

public sealed partial class RegisteredTransportTests
{
    private const string SharedSenderSyntheticOperation = "management.fixture.projection.read";
    private static ManagementOperationDescriptor SharedSenderSyntheticDescriptor => new(SharedSenderSyntheticOperation, "GET",
        "/api/management-operations/tenants/{tenantId:guid}/fixture/{resourceId:guid}", new(typeof(SharedSenderSyntheticRequest), "fixture.read.request"),
        new(typeof(SharedSenderSyntheticResult), "fixture.read.result"), typeof(IManagementOperationHandler<SharedSenderSyntheticRequest, SharedSenderSyntheticResult>),
        [SharedSenderSyntheticOperation], 0, SharedSenderSyntheticOperation);
    public sealed record SharedSenderSyntheticRequest;
    public sealed record SharedSenderSyntheticResult(Guid TenantId, Guid ResourceId, int Count);
    public sealed class SharedSenderSyntheticHandler : IManagementOperationHandler<SharedSenderSyntheticRequest, SharedSenderSyntheticResult>
    {
        public Task<SharedSenderSyntheticResult> ExecuteAsync(ManagementOperationContext scope, SharedSenderSyntheticRequest request, CancellationToken ct = default)
            => Task.FromResult(new SharedSenderSyntheticResult(scope.TenantId, scope.ResourceId, 12));
    }
    [TestMethod]
    public async Task SharedSenderResolvesBothPortsToOneScopedClientWithoutDoubleSigning()
    {
        await using var f = await Fixture.Create(); var services = new ServiceCollection(); services.AddSingleton(f.Client);
        services.AddIBeamManagementRegisteredOperationsPortal(new ConfigurationBuilder().Build()); using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<IManagementTargetOperationSender>();
        Assert.AreSame(sender, scope.ServiceProvider.GetRequiredService<IManagementOperationClient>());
        var assertions = 0; f.BeforeSend = request => { if (request.Method == HttpMethod.Post) {
            Assert.AreEqual(1, request.Headers.GetValues("X-IBeam-Management-Actor").Count()); assertions++;
        } };
        var initial = await f.Read(); var result = await sender.ExecuteAsync<ManagementLicenseSuspendRequest, ManagementLicenseTransitionResult>(f.AppId,
            ManagementLicenseOperations.Suspend, f.Tenant, f.License, new(f.License, initial.Version), "shared-sender", initial.Version);
        Assert.AreEqual("suspended", result.Status); Assert.AreEqual(1, assertions); Assert.AreEqual(1, f.WriteCount);
        var proof = new JwtSecurityTokenHandler().ReadJwtToken(f.LastWrite!.Assertion);
        Assert.AreEqual(f.Tenant.ToString("D"), proof.Claims.Single(x => x.Type == "tenant").Value);
        Assert.AreEqual(f.License.ToString("D"), proof.Claims.Single(x => x.Type == "resource").Value);
        Assert.AreEqual(result.OperationId.ToString("D"), proof.Claims.Single(x => x.Type == "operation_id").Value);
        Assert.AreEqual("shared-sender", f.LastWrite.Key); Assert.AreEqual(initial.Version, f.LastWrite.Version);
    }
    [TestMethod]
    public async Task SharedSenderExecutesOnlyTheFixedTypedSyntheticRouteWithVerifiedScope()
    {
        await using var f = await Fixture.Create(includeSynthetic: true); IManagementTargetOperationSender sender = f.Client; var requests = new List<string>();
        f.BeforeSend = request => { requests.Add(request.RequestUri!.AbsolutePath); if (!request.RequestUri.AbsolutePath.EndsWith("/info", StringComparison.Ordinal))
            Assert.AreEqual(1, request.Headers.GetValues("X-IBeam-Management-Actor").Count()); };
        var result = await sender.ExecuteAsync<SharedSenderSyntheticRequest, SharedSenderSyntheticResult>(f.AppId, SharedSenderSyntheticOperation, f.Tenant, f.License, new(), "", null);
        Assert.AreEqual(new(f.Tenant, f.License, 12), result);
        Assert.AreEqual($"/api/management-operations/tenants/{f.Tenant:D}/fixture/{f.License:D}", requests.Last()); Assert.AreEqual(0, f.WriteCount);
    }
    [TestMethod]
    [DataRow("human")] [DataRow("machine")] [DataRow("capability")]
    public async Task SharedSenderCannotBorrowExistingBuiltinGrantsForTheCustomRoute(string removed)
    {
        await using var f = await Fixture.Create(includeSynthetic: true); IManagementTargetOperationSender sender = f.Client;
        if (removed == "human") f.Assignment = f.Assignment with { Permissions = f.Assignment.Permissions.Where(x => x != SharedSenderSyntheticOperation).ToArray() };
        if (removed == "machine") f.Credential = f.Credential with { Capabilities = f.Credential.Capabilities.Where(x => x != SharedSenderSyntheticOperation).ToArray() };
        if (removed == "capability") f.Connection = f.Connection with { Operations = f.Connection.Operations! with { Info = f.Connection.Operations.Info! with {
            Capabilities = f.Connection.Operations.Info.Capabilities.Where(x => x.Operation != SharedSenderSyntheticOperation).ToArray() } } };
        Task<SharedSenderSyntheticResult> Call() => sender.ExecuteAsync<SharedSenderSyntheticRequest, SharedSenderSyntheticResult>(f.AppId,
            SharedSenderSyntheticOperation, f.Tenant, f.License, new(), "", null);
        // A removed machine grant also removes fresh discovery, which is an unavailable capability.
        if (removed == "machine") await Assert.ThrowsAsync<ManagementOperationUnavailableException>(Call);
        else await Assert.ThrowsAsync<ManagementAccessDeniedException>(Call);
        Assert.AreEqual(0, f.WriteCount);
    }
    [TestMethod]
    [DataRow("https://untrusted.example/api")] [DataRow("management.unknown")]
    public async Task SharedSenderRejectsCallerUrlsAndUnknownOperationsBeforeReadingSecrets(string operation)
    {
        await using var f = await Fixture.Create(); IManagementTargetOperationSender sender = f.Client;
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => sender.ExecuteAsync<SharedSenderSyntheticRequest, SharedSenderSyntheticResult>(f.AppId,
            operation, f.Tenant, f.License, new(), "", null)); Assert.AreEqual(0, f.SecretReads);
    }
    [TestMethod]
    public async Task SharedSenderRejectsUndeclaredRequestTypeAndWriteMetadataOnARead()
    {
        await using var f = await Fixture.Create(includeSynthetic: true); IManagementTargetOperationSender sender = f.Client;
        await Assert.ThrowsAsync<ManagementOperationInvalidException>(() => sender.ExecuteAsync<ManagementLicenseReadRequest, SharedSenderSyntheticResult>(f.AppId,
            SharedSenderSyntheticOperation, f.Tenant, f.License, new(), "", null));
        await Assert.ThrowsAsync<ManagementOperationInvalidException>(() => sender.ExecuteAsync<SharedSenderSyntheticRequest, SharedSenderSyntheticResult>(f.AppId,
            SharedSenderSyntheticOperation, f.Tenant, f.License, new(), "caller-write", "version")); Assert.AreEqual(0, f.SecretReads);
    }
    [TestMethod]
    [DataRow("redirect")] [DataRow("oversize")] [DataRow("extra-field")]
    public async Task SharedSenderRejectsUntrustedResponsesWithoutFollowingOrLeakingThem(string problem)
    {
        await using var f = await Fixture.Create(includeSynthetic: true); IManagementTargetOperationSender sender = f.Client; var calls = 0;
        f.BeforeSend = _ => calls++;
        f.AfterSend = (request, response) => {
            if (request.RequestUri!.AbsolutePath.EndsWith("/info", StringComparison.Ordinal)) return Task.FromResult(response);
            response.Dispose(); var replacement = new HttpResponseMessage(problem == "redirect" ? HttpStatusCode.Found : HttpStatusCode.OK);
            if (problem == "redirect") replacement.Headers.Location = new("https://169.254.169.254/PRIVATE-REDIRECT");
            else replacement.Content = problem == "oversize" ? new StringContent(new string('X', 100000)) :
                JsonContent.Create(new { success = true, data = new { tenantId = f.Tenant, resourceId = f.License, count = 12, clinicalData = "PRIVATE-CUSTOMER-DATA" } });
            return Task.FromResult(replacement);
        };
        var error = await Assert.ThrowsAsync<ManagementOperationUnavailableException>(() => sender.ExecuteAsync<SharedSenderSyntheticRequest, SharedSenderSyntheticResult>(f.AppId,
            SharedSenderSyntheticOperation, f.Tenant, f.License, new(), "", null));
        Assert.DoesNotContain("PRIVATE-", error.ToString()); Assert.AreEqual(2, calls);
    }
    [TestMethod]
    public async Task SharedSenderPreservesUnknownWriteAndReadonlyOriginalReceiptRecovery()
    {
        await using var f = await Fixture.Create(); IManagementTargetOperationSender sender = f.Client; var initial = await f.Read();
        f.AfterSend = (request, response) => { if (request.Method == HttpMethod.Post) { response.Dispose(); throw new IOException("PRIVATE-LOST-REPLY"); } return Task.FromResult(response); };
        var error = await Assert.ThrowsAsync<ManagementOperationUncertainException>(() => sender.ExecuteAsync<ManagementLicenseSuspendRequest, ManagementLicenseTransitionResult>(f.AppId,
            ManagementLicenseOperations.Suspend, f.Tenant, f.License, new(f.License, initial.Version), "same-intent", initial.Version));
        Assert.DoesNotContain("PRIVATE-", error.ToString()); f.AfterSend = null;
        var receipt = await sender.ReadReceiptAsync<ManagementLicenseTransitionResult>(f.AppId, ManagementLicenseOperations.Suspend, f.Tenant, f.License, error.OperationId);
        Assert.AreEqual("suspended", receipt.Status); Assert.AreEqual(1, f.WriteCount);
    }
    [TestMethod]
    public async Task SharedSenderHonorsCallerCancellationWithoutANativeWrite()
    {
        await using var f = await Fixture.Create(); IManagementTargetOperationSender sender = f.Client; using var ct = new CancellationTokenSource(); ct.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => sender.ExecuteAsync<ManagementLicenseReadRequest, ManagementLicenseStatus>(f.AppId,
            ManagementLicenseOperations.Read, f.Tenant, f.License, new(), "", null, ct.Token)); Assert.AreEqual(0, f.WriteCount);
    }
    [TestMethod]
    public async Task SharedSenderNetworkPrimitiveRetainsTlsAndPinsTheValidatedPublicIp()
    {
        var options = Options.Create(new ManagementConnectionOptions { ApprovedOrigins = ["https://fixture.example"] });
        var dns = new Mock<IManagementTargetDnsResolver>(); dns.Setup(x => x.ResolveAsync("fixture.example", It.IsAny<CancellationToken>())).ReturnsAsync([IPAddress.Parse("8.8.8.8")]);
        var sockets = new Mock<IManagementTargetSocketConnector>(); IPEndPoint? endpoint = null;
        sockets.Setup(x => x.ConnectAsync(It.IsAny<IPEndPoint>(), It.IsAny<CancellationToken>())).Returns<IPEndPoint, CancellationToken>((ep, _) => { endpoint = ep; return ValueTask.FromResult<Stream>(new MemoryStream()); });
        var network = new ManagementConnectionNetworkPolicy(options, dns.Object, sockets.Object);
        using var handler = ManagementTargetHttpTransport.CreateHandler(network, new("https://fixture.example"));
        Assert.IsFalse(handler.AllowAutoRedirect); Assert.IsFalse(handler.UseProxy); Assert.IsFalse(handler.UseCookies);
        Assert.AreEqual(DecompressionMethods.None, handler.AutomaticDecompression); Assert.IsNull(handler.ActivityHeadersPropagator);
        Assert.IsNull(handler.SslOptions.RemoteCertificateValidationCallback); Assert.AreEqual(TimeSpan.FromSeconds(5), handler.ConnectTimeout);
        using var client = new HttpClient(handler); await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://fixture.example/api/management-operations/info"));
        Assert.AreEqual(new IPEndPoint(IPAddress.Parse("8.8.8.8"), 443), endpoint);
        Assert.Throws<ManagementConnectionInvalidException>(() => ManagementTargetHttpTransport.CreateClient(network, new("https://untrusted.example")));
    }
}
