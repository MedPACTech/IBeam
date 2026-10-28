using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.Api;
using IBeam.Management.Entities;
using IBeam.Management.Operations;
using IBeam.Management.Services;
using IBeam.Management.Services.Audit;
using IBeam.Management.Services.Credentials;
using IBeam.Management.Services.CustomerIdentity.Delegation;
using IBeam.Management.TenantData;
using IBeam.Services.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace IBeam.Tests.Management.TenantData;

[TestClass]
public sealed class PortalExportTests
{
    [TestMethod]
    public async Task PortalSubmitsAndPollsOneSafeJobThroughRealSignedNativeTransport()
    {
        await using var f = await Fixture.Create();
        using var submitted = await f.Submit(); Assert.AreEqual(HttpStatusCode.Accepted, submitted.StatusCode);
        Assert.IsNotNull(submitted.Headers.Location); Assert.AreEqual("no-store", submitted.Headers.CacheControl!.ToString());
        var accepted = (await submitted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.AreEqual("accepted", accepted.GetProperty("outcome").GetString()); Assert.AreEqual("requested", accepted.GetProperty("job").GetProperty("state").GetString());
        using var replay = await f.Submit(); Assert.AreEqual(1, f.Target.Journal.Count);
        var execution = (await f.Target.Journal.ListPageAsync(new("fixture", "Testing", f.Target.Org, f.Target.AppId, f.Target.Realm), 1, null)).Items.Single();
        using (var scope = f.Target.Server.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<IManagementTenantExportWorker>().RunAsync(execution);
        using var status = await f.Http.GetAsync(submitted.Headers.Location);
        Assert.AreEqual(HttpStatusCode.OK, status.StatusCode);
        Assert.AreEqual("succeeded", (await status.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("job").GetProperty("state").GetString());
        Assert.DoesNotContain("PRIVATE-", await status.Content.ReadAsStringAsync());
        Assert.DoesNotContain(f.Target.Secret, await status.Content.ReadAsStringAsync());
        Assert.IsNull(f.Portal.Services.GetService<IManagementTenantExportJobService>());
    }
    [TestMethod]
    [DataRow("assignment")] [DataRow("support-user")] [DataRow("app")]
    public async Task LiveSupportRevocationDeniesBeforeMachineSecretResolution(string change)
    {
        await using var f = await Fixture.Create();
        if (change == "assignment") f.Assignment = f.Assignment with { RevokedUtc = DateTimeOffset.UtcNow };
        if (change == "support-user") f.EnabledUser = false;
        if (change == "app") f.Application = f.Application with { IsEnabled = false };
        using var response = await f.Submit(); Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.AreEqual(0, f.SecretReads); Assert.AreEqual(0, f.Target.Journal.Count);
    }
    [TestMethod]
    public async Task BorrowedTenantPermissionAndCustomerBearerDoNotGrantExport()
    {
        await using var f = await Fixture.Create();
        f.Assignment = f.Assignment with { Permissions = [IBeam.Management.CustomerIdentity.ManagementCustomerIdentityOperations.TenantsStatus] };
        using var denied = await f.Submit(); Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode); Assert.AreEqual(0, f.SecretReads);
        f.Http.DefaultRequestHeaders.Authorization = new("Bearer", "customer");
        using var unauthorized = await f.Submit(); Assert.AreEqual(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }
    [TestMethod]
    [DataRow("unknown-field")] [DataRow("wildcard")] [DataRow("missing-key")]
    public async Task StrictPortalBodyAndVersionHeadersCannotSpecifyCustomerRecipientsOrDestinations(string change)
    {
        await using var f = await Fixture.Create();
        using var response = await f.Submit(change == "unknown-field" ? new { policyKey = "enterprise", recipient = "PRIVATE-EMAIL", destination = "https://private.example" } : new { policyKey = "enterprise" },
            change == "wildcard" ? "*" : "tenant-1", change == "missing-key" ? null : "portal-export-intent");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode); Assert.AreEqual(0, f.Target.Journal.Count);
        Assert.DoesNotContain("PRIVATE-", await response.Content.ReadAsStringAsync());
    }
    [TestMethod]
    [DataRow("lost")] [DataRow("semantic-substitution")]
    public async Task UnknownWritePreservesTheOriginalJobReferenceAndPollingNeverCreatesAnotherExport(string mode)
    {
        await using var f = await Fixture.Create();
        f.AfterSend = (request, response) =>
        {
            if (request.Method != HttpMethod.Post) return Task.FromResult(response);
            response.Dispose();
            if (mode == "lost") throw new IOException("PRIVATE-LOST-REPLY");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true,
                data = ExportContractTests.Result(f.Target.Tenant, Guid.NewGuid()) }) });
        };
        using var unknown = await f.Submit(); Assert.AreEqual(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.IsNotNull(unknown.Headers.Location); Assert.AreEqual(1, f.Writes); Assert.AreEqual(1, f.Target.Journal.Count);
        var body = (await unknown.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.AreEqual("unknown", body.GetProperty("outcome").GetString()); Assert.AreEqual(JsonValueKind.Null, body.GetProperty("job").ValueKind);
        f.AfterSend = null; using var recovered = await f.Http.GetAsync(unknown.Headers.Location);
        Assert.AreEqual(HttpStatusCode.OK, recovered.StatusCode); Assert.AreEqual(1, f.Writes);
        f.EnabledUser = false; using var revoked = await f.Http.GetAsync(unknown.Headers.Location);
        Assert.AreEqual(HttpStatusCode.Forbidden, revoked.StatusCode); Assert.AreEqual(1, f.Writes);
    }
    [TestMethod]
    [DataRow("machine")] [DataRow("capability")]
    public async Task CurrentMachineOrTargetRevocationMakesTheOperationUnavailableWithoutSubmission(string change)
    {
        await using var f = await Fixture.Create();
        if (change == "machine") f.Target.Credential = f.Target.Credential with { RevokedUtc = DateTimeOffset.UtcNow };
        else f.Target.Server.Services.GetRequiredService<IOptions<ManagementOperationTargetOptions>>().Value.EnabledOperations = [];
        using var response = await f.Submit(); Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.AreEqual(0, f.Writes); Assert.AreEqual(0, f.Target.Journal.Count);
    }
    [TestMethod]
    public async Task BaseApiInstallsNoExportOrArtifactDownloadRoute()
    {
        await using var f = await Fixture.Create(install: false);
        using var absent = await f.Submit(); Assert.AreEqual(HttpStatusCode.NotFound, absent.StatusCode);
        using var download = await f.Http.GetAsync(f.Path + "/" + Guid.NewGuid() + "/download"); Assert.AreEqual(HttpStatusCode.NotFound, download.StatusCode);
    }
    [TestMethod]
    [DataRow("failure-code")] [DataRow("coverage")] [DataRow("state")]
    public async Task UnsafeNativeMetadataBecomesScopedUnknownWithoutEchoingItsContent(string change)
    {
        await using var f = await Fixture.Create();
        f.AfterSend = async (request, response) =>
        {
            if (request.Method != HttpMethod.Post) return response;
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var result = json.GetProperty("data").Deserialize<ManagementTenantExportResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            var job = result.Job!;
            job = change switch {
                "failure-code" => job with { FailureCode = "PRIVATE-FAILURE-CONTENT" },
                "coverage" => job with { Coverage = [job.Coverage[0] with { Key = "PRIVATE-CUSTOMER-FILENAME" }, ..job.Coverage[1..]] },
                _ => job with { State = "PRIVATE-STATE" }
            };
            response.Dispose(); return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true, data = result with { Job = job } }) };
        };
        using var response = await f.Submit(); Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
        Assert.DoesNotContain("PRIVATE-", await response.Content.ReadAsStringAsync());
        Assert.AreEqual("unknown", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("outcome").GetString());
        Assert.AreEqual(1, f.Writes); Assert.AreEqual(1, f.Target.Journal.Count);
    }
    [TestMethod]
    public async Task AnotherApplicationIdentifierCannotResolveTheSelectedConnectionOrCreateAJob()
    {
        await using var f = await Fixture.Create();
        using var request = new HttpRequestMessage(HttpMethod.Post, f.Path.Replace(f.Target.AppId.ToString("D"), Guid.NewGuid().ToString("D"))) { Content = JsonContent.Create(new { policyKey = "enterprise" }) };
        request.Headers.TryAddWithoutValidation("If-Match", "tenant-1"); request.Headers.Add("Idempotency-Key", "foreign-app-intent");
        using var response = await f.Http.SendAsync(request); Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.AreEqual(0, f.SecretReads); Assert.AreEqual(0, f.Target.Journal.Count);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public ExportTargetHttpTests.Fixture Target { get; private set; } = null!;
        public WebApplication Portal { get; private set; } = null!; public HttpClient Http { get; private set; } = null!;
        public ManagementApplicationEntity Application { get; set; } = null!;
        public ManagementApplicationAccessEntity Assignment { get; set; } = null!;
        public bool EnabledUser = true; public int SecretReads, Writes;
        public Func<HttpRequestMessage, HttpResponseMessage, Task<HttpResponseMessage>>? AfterSend;
        public string Path => $"/api/management/applications/{Target.AppId:D}/tenants/{Target.Tenant:D}/exports";
        public static async Task<Fixture> Create(bool install = true)
        {
            var f = new Fixture { Target = await ExportTargetHttpTests.Fixture.Create(standardJobs: true) };
            var registry = new ManagementOperationRegistry([ManagementTenantExportOperation.Descriptor]);
            var connectionId = Guid.NewGuid();
            f.Application = new() { OrganizationId = f.Target.Org, ApplicationId = f.Target.AppId, ApplicationKey = "fixture", Environment = "Testing", BaseUrl = "https://fixture.example", ActiveCredentialId = connectionId, Version = "app-1" };
            f.Assignment = new() { OrganizationId = f.Target.Org, ApplicationId = f.Target.AppId, UserId = f.Target.User, Permissions = [ManagementTenantDataOperations.Export], Version = "assignment-1" };
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", f.Target.User.ToString("D")), new(ManagementClaimTypes.SubjectType, ManagementSubjectTypes.Human),
                new(ManagementClaimTypes.OrganizationId, f.Target.Org.ToString("D")), new(ManagementClaimTypes.Permission, ManagementOperations.IdentityRead)], "trusted-support"));
            var principals = new Mock<IManagementPrincipalProvider>(); principals.Setup(x => x.GetPrincipal()).Returns(principal);
            var apps = new Mock<IManagementApplicationStateProvider>(); apps.Setup(x => x.GetAsync(f.Target.Org, f.Target.AppId, It.IsAny<CancellationToken>())).ReturnsAsync(() => f.Application);
            var assignments = new Mock<IManagementApplicationAccessRepository>(); assignments.Setup(x => x.GetAsync(f.Target.Org, f.Target.AppId, f.Target.User, It.IsAny<CancellationToken>())).ReturnsAsync(() => f.Assignment);
            var users = new Mock<IManagementSupportUserStateProvider>(); users.Setup(x => x.GetAsync(f.Target.Org, f.Target.User, It.IsAny<CancellationToken>())).ReturnsAsync(() => new(f.Target.Org, f.Target.User, f.EnabledUser, false));
            var info = new ManagementRegisteredOperationInfo("fixture", "Testing", f.Target.Realm, "dedicated-app", 1, registry.Capabilities([ManagementTenantDataOperations.Export], [ManagementTenantDataOperations.Export], _ => true).ToArray());
            var connection = new ManagementApplicationCredentialEntity { OrganizationId = f.Target.Org, ApplicationId = f.Target.AppId, CredentialId = connectionId, BaseUrl = f.Application.BaseUrl,
                SecretReference = "private-reference", Capabilities = [ManagementRegisteredOperations.Info, ManagementTenantDataOperations.Export], Operations = new("available", info) };
            var connections = new Mock<IManagementApplicationCredentialRepository>(); connections.Setup(x => x.GetAsync(f.Target.Org, f.Target.AppId, connectionId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
            var secrets = new Mock<IManagementSecretStore>(); secrets.Setup(x => x.ReadAsync(new(f.Target.Org, f.Target.AppId), "private-reference", It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => { f.SecretReads++; return new(f.Target.Secret); });
            var executor = new ServiceOperationExecutor();
            var access = new ManagementApplicationAccessService(assignments.Object, apps.Object, users.Object, principals.Object, executor, registry);
            var available = new ManagementRegisteredOperationAvailabilityService(registry, principals.Object, apps.Object, connections.Object, access, executor);
            var signing = new Mock<IManagementActorSigningKeyProvider>(); signing.Setup(x => x.GetSigningCredentials()).Returns(() => f.Target.Signing);
            var options = new Monitor<ManagementRegisteredOperationClientOptions>(new() { Issuer = "fixture-portal", OperationIdKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), Targets = [new() { ApplicationId = f.Target.AppId, Audience = "fixture-target" }] });
            var networkOptions = Options.Create(new ManagementConnectionOptions { ApprovedOrigins = [f.Application.BaseUrl] });
            var network = new ManagementConnectionNetworkPolicy(networkOptions, Mock.Of<IManagementTargetDnsResolver>(), Mock.Of<IManagementTargetSocketConnector>());
            var client = new ManagementRegisteredOperationClient(registry, principals.Object, access, available, apps.Object, connections.Object, secrets.Object, new ManagementCredentialSecretCodec(),
                signing.Object, new ManagementAuditContext(), network, networkOptions, options, TimeProvider.System, executor)
                { TestTransportFactory = () => new Bridge(f) { InnerHandler = f.Target.Server.GetTestServer().CreateHandler() } };
            var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["IBeam:Management:Api:AuthenticationScheme"] = "support" });
            builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, Auth>("support", _ => { });
            builder.Services.AddSingleton<IManagementOperationClient>(client);
            if (install) builder.Services.AddIBeamManagementPortalTenantExportApi(builder.Configuration);
            else builder.Services.AddIBeamManagementApi(builder.Configuration);
            f.Portal = builder.Build(); f.Portal.UseAuthentication(); f.Portal.UseAuthorization(); f.Portal.MapControllers();
            await f.Portal.StartAsync(); f.Http = f.Portal.GetTestClient(); f.Http.DefaultRequestHeaders.Authorization = new("Bearer", "support"); return f;
        }
        public async Task<HttpResponseMessage> Submit(object? body = null, string version = "tenant-1", string? key = "portal-export-intent")
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = JsonContent.Create(body ?? new { policyKey = "enterprise" }) };
            request.Headers.TryAddWithoutValidation("If-Match", version); if (key is not null) request.Headers.Add("Idempotency-Key", key);
            return await Http.SendAsync(request);
        }
        public async ValueTask DisposeAsync() { Http.Dispose(); await Portal.DisposeAsync(); await Target.DisposeAsync(); }
        private sealed class Bridge(Fixture f) : DelegatingHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                if (request.Method == HttpMethod.Post) f.Writes++;
                var response = await base.SendAsync(request, ct);
                return f.AfterSend is null ? response : await f.AfterSend(request, response);
            }
        }
    }
    private sealed class Auth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    { protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(Request.Headers.Authorization == "Bearer support" ?
        AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "support")], Scheme.Name)), Scheme.Name)) : AuthenticateResult.Fail("Support authentication required.")); }
    private sealed class Monitor<T>(T value) : IOptionsMonitor<T>
    { public T CurrentValue => value; public T Get(string? name) => value; public IDisposable? OnChange(Action<T, string?> listener) => null; }
}
