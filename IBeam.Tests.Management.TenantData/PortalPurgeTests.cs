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
using IBeam.Management.TenantData.Target.Api;
using IBeam.Management.TenantData.Target.Services;
using IBeam.Management.Operations.Target.Api;
using System.Collections.Concurrent;
using Microsoft.IdentityModel.Tokens;

namespace IBeam.Tests.Management.TenantData;

[TestClass]
public sealed class PortalPurgeTests
{
    [TestMethod]
    public async Task PreflightConfirmationAndReadonlyProgressUseOneDurableJobAndSafeInventory()
    {
        await using var f = await Fixture.Create(); using var preview = await f.Preflight();
        Assert.AreEqual(HttpStatusCode.OK, preview.StatusCode); Assert.AreEqual("no-store", preview.Headers.CacheControl!.ToString());
        Assert.IsNotNull(f.Plan); Assert.AreEqual(0, f.Target.Native.Native.PurgeCalls); Assert.IsTrue(await f.Target.Native.Native.DataExists());
        using var confirm = await f.Submit(); Assert.AreEqual(HttpStatusCode.Accepted, confirm.StatusCode); Assert.IsNotNull(confirm.Headers.Location);
        var accepted = await Data(confirm); var job = accepted.OperationId;
        Assert.AreEqual(f.Plan.PlanId, accepted.Job!.PlanId); Assert.AreEqual("requested", accepted.Job.State);
        using var replay = await f.Submit(); Assert.AreEqual(job, (await Data(replay)).OperationId); Assert.AreEqual(0, f.Target.Native.Native.PurgeCalls);
        await f.Target.Run(job);
        using var found = await f.Http.GetAsync(confirm.Headers.Location); Assert.AreEqual(HttpStatusCode.OK, found.StatusCode);
        var complete = await Data(found); Assert.AreEqual("succeeded", complete.Job!.State); Assert.IsTrue(complete.Job.Coverage.Any(x => x.Disposition == "retained"));
        Assert.IsTrue(await f.Target.Native.Native.OtherDataExists()); Assert.IsTrue(await f.Target.Native.Native.GlobalDataExists());
        Assert.DoesNotContain("PRIVATE-", await found.Content.ReadAsStringAsync()); Assert.DoesNotContain(f.Target.Secret, await found.Content.ReadAsStringAsync());
        Assert.IsNull(f.Portal.Services.GetService<IManagementTenantPurgeWorker>()); Assert.IsNull(f.Portal.Services.GetService<IManagementTenantPurgeJobService>());
        Assert.IsTrue(f.Audits.Any(x => x.Operation == "management.tenants.delete.portal.confirmation" && x.SubjectId == f.Plan.PlanId));
        Assert.IsTrue(f.Audits.Any(x => x.Operation == "management.tenants.delete.portal.accepted" && x.SubjectId == job));
        Assert.IsTrue(f.Audits.Any(x => x.Operation == "management.tenants.delete.portal.succeeded" && x.SubjectId == job && x.Succeeded));
        Assert.IsTrue(f.Audits.Where(x => x.Operation.StartsWith("management.tenants.delete", StringComparison.Ordinal))
            .All(x => x.OrganizationId == f.Target.Org && x.ActorUserId == f.Target.User && x.ApplicationId == f.Target.AppId));
        Assert.DoesNotContain(f.Plan.ConfirmationBinding, JsonSerializer.Serialize(f.Audits));
    }
    [TestMethod]
    [DataRow("assignment")] [DataRow("support-user")] [DataRow("app")] [DataRow("machine")] [DataRow("capability")]
    public async Task CurrentRevocationBlocksPollingAndCannotResumeOrCreateWork(string change)
    {
        await using var f = await Fixture.Create(); using var preview = await f.Preflight(); using var confirm = await f.Submit();
        var writes = f.Writes;
        switch (change) {
            case "assignment": f.Assignment = f.Assignment with { RevokedUtc = DateTimeOffset.UtcNow }; break;
            case "support-user": f.EnabledUser = false; break;
            case "app": f.Application = f.Application with { IsEnabled = false }; break;
            case "machine": f.Target.Credential = f.Target.Credential with { RevokedUtc = DateTimeOffset.UtcNow }; break;
            default: f.Target.Server.Services.GetRequiredService<IOptions<ManagementOperationTargetOptions>>().Value.EnabledOperations = []; break;
        }
        using var poll = await f.Http.GetAsync(confirm.Headers.Location);
        Assert.IsTrue(poll.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.ServiceUnavailable); Assert.AreEqual(writes, f.Writes);
        using var resume = await f.Http.PostAsJsonAsync(confirm.Headers.Location + "/resume", new { }); Assert.AreEqual(HttpStatusCode.NotFound, resume.StatusCode);
        Assert.AreEqual(0, f.Target.Native.Native.PurgeCalls);
    }
    [TestMethod]
    [DataRow("lost")] [DataRow("job-substitution")] [DataRow("plan-substitution")] [DataRow("unsafe-state")]
    public async Task LostOrInvalidConfirmationRepliesKeepOriginalStatusReferenceWithoutAnotherMutation(string mode)
    {
        await using var f = await Fixture.Create(); using var preview = await f.Preflight();
        f.AfterSend = async (request, response) => {
            if (request.Method != HttpMethod.Post) return response;
            var data = await Data(response); response.Dispose();
            if (mode == "lost") throw new IOException("PRIVATE-LOST-REPLY");
            data = mode switch { "job-substitution" => data with { OperationId = Guid.NewGuid() },
                "plan-substitution" => data with { Job = data.Job! with { PlanId = Guid.NewGuid() } },
                _ => data with { Job = data.Job! with { State = "PRIVATE-STATE" } } };
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true, data }) };
        };
        using var unknown = await f.Submit(); Assert.AreEqual(HttpStatusCode.Accepted, unknown.StatusCode);
        var original = await Data(unknown); Assert.AreEqual("unknown", original.Outcome); Assert.IsNull(original.Job); Assert.IsNotNull(unknown.Headers.Location);
        var writes = f.Writes; f.AfterSend = null;
        using var recovered = await f.Http.GetAsync(unknown.Headers.Location); Assert.AreEqual(HttpStatusCode.OK, recovered.StatusCode);
        Assert.AreEqual(original.OperationId, (await Data(recovered)).OperationId); Assert.AreEqual(writes, f.Writes); Assert.AreEqual(0, f.Target.Native.Native.PurgeCalls);
        Assert.IsTrue(f.Audits.Any(x => x.Operation == "management.tenants.delete.portal.unknown" && x.SubjectId == original.OperationId && !x.Succeeded));
        Assert.DoesNotContain("PRIVATE-", await unknown.Content.ReadAsStringAsync());
    }
    [TestMethod]
    [DataRow("binding")] [DataRow("expired")] [DataRow("version")] [DataRow("plan")] [DataRow("reused-plan")]
    public async Task SubstitutedStaleOrConsumedConfirmationCannotStartAnotherPurge(string change)
    {
        await using var f = await Fixture.Create(); using var preview = await f.Preflight(); var plan = f.Plan!;
        if (change == "expired") f.Target.Native.Clock.Now = plan.ExpiresUtc;
        if (change == "reused-plan") { using var accepted = await f.Submit(); Assert.AreEqual(HttpStatusCode.Accepted, accepted.StatusCode); }
        var body = new { policyKey = plan.PolicyKey, planId = change == "plan" ? Guid.NewGuid() : plan.PlanId,
            confirmationBinding = change == "binding" ? new string('A', 64) : plan.ConfirmationBinding };
        using var response = await f.Submit(body, change == "version" ? "v2" : "v1", change == "reused-plan" ? "another-job" : "portal-purge-intent");
        Assert.IsTrue(response.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict);
        Assert.AreEqual(0, f.Target.Native.Native.PurgeCalls); Assert.IsTrue(await f.Target.Native.Native.DataExists());
    }
    [TestMethod]
    [DataRow("partial")] [DataRow("failed")] [DataRow("unknown")]
    public async Task ProgressReportsPartialFailureAndUnknownWithoutClaimingCompletedDeletion(string mode)
    {
        await using var f = await Fixture.Create(); using var preview = await f.Preflight(); using var confirm = await f.Submit(); var result = await Data(confirm);
        if (mode == "partial") f.Target.Native.Native.CoverageMode = "partial";
        if (mode == "failed") f.Target.Native.Native.CoverageMode = "missing";
        if (mode == "unknown") f.Target.Native.Native.LoseReply = "purging";
        await f.Target.Run(result.OperationId);
        using var status = await f.Http.GetAsync(confirm.Headers.Location);
        Assert.AreEqual(mode == "unknown" ? HttpStatusCode.Accepted : HttpStatusCode.OK, status.StatusCode);
        var state = mode == "partial" ? "partially-completed" : mode;
        Assert.AreEqual(state, (await Data(status)).Job!.State);
        Assert.IsTrue(f.Audits.Any(x => x.Operation == "management.tenants.delete.portal." + state && !x.Succeeded));
        Assert.DoesNotContain("PRIVATE-", JsonSerializer.Serialize(f.Audits));
    }
    [TestMethod]
    [DataRow("unknown-field")] [DataRow("wildcard")] [DataRow("missing-key")]
    public async Task StrictRequestsCannotSupplyStorageSelectorsOrWildcardVersions(string change)
    {
        await using var f = await Fixture.Create();
        using var response = await f.Submit(change == "unknown-field" ? new { policyKey = "enterprise", planId = Guid.NewGuid(), confirmationBinding = new string('A', 64), table = "PRIVATE-TABLE" } :
            new { policyKey = "enterprise", planId = Guid.NewGuid(), confirmationBinding = new string('A', 64) }, change == "wildcard" ? "*" : "v1", change == "missing-key" ? null : "strict-intent");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode); Assert.DoesNotContain("PRIVATE-", await response.Content.ReadAsStringAsync());
    }
    [TestMethod]
    public async Task BorrowedExportPermissionForeignAppAndCustomerAuthenticationGrantNoPurgeAuthority()
    {
        await using var f = await Fixture.Create(); f.Assignment = f.Assignment with { Permissions = [ManagementTenantDataOperations.Export] };
        using var denied = await f.Preflight(); Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode); Assert.AreEqual(0, f.SecretReads);
        using var request = new HttpRequestMessage(HttpMethod.Post, f.Path.Replace(f.Target.AppId.ToString("D"), Guid.NewGuid().ToString("D")).Replace("/purges", "/purge-plans")) { Content = JsonContent.Create(new { policyKey = "enterprise" }) };
        request.Headers.TryAddWithoutValidation("If-Match", "v1"); request.Headers.Add("Idempotency-Key", "foreign-app");
        using var foreign = await f.Http.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Forbidden, foreign.StatusCode);
        f.Http.DefaultRequestHeaders.Authorization = new("Bearer", "customer"); using var unauthorized = await f.Preflight(); Assert.AreEqual(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }
    [TestMethod]
    public async Task BaseApiDoesNotInstallDestructiveRoutes()
    {
        await using var f = await Fixture.Create(install: false); using var absent = await f.Preflight(); Assert.AreEqual(HttpStatusCode.NotFound, absent.StatusCode);
        Assert.AreEqual(0, f.Writes);
    }
    [TestMethod]
    [DataRow("lost")] [DataRow("binding")] [DataRow("inventory")]
    public async Task UnknownReadonlyPreflightRequiresAValidRecoveredPlanBeforeConfirmation(string mode)
    {
        await using var f = await Fixture.Create();
        f.AfterSend = async (request, response) => {
            if (request.Method != HttpMethod.Post) return response;
            var data = await Data(response); response.Dispose();
            if (mode == "lost") throw new IOException("PRIVATE-PREVIEW-REPLY");
            data = data with { Plan = mode == "binding" ? data.Plan! with { ConfirmationBinding = "PRIVATE-BINDING" } :
                data.Plan! with { Categories = [new("global-accounts", "identity-metadata", "delete"), ..data.Plan.Categories.Where(x => x.Key != "global-accounts")] } };
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true, data }) };
        };
        using var unknown = await f.Preflight(); Assert.AreEqual(HttpStatusCode.Accepted, unknown.StatusCode); var original = await Data(unknown);
        Assert.AreEqual("unknown", original.Outcome); Assert.IsNull(original.Plan); Assert.IsNull(unknown.Headers.Location);
        f.AfterSend = null; using var recovered = await f.Preflight(); Assert.AreEqual(HttpStatusCode.OK, recovered.StatusCode);
        Assert.AreEqual(original.OperationId, f.Plan!.PlanId); Assert.AreEqual(0, f.Target.Native.Native.PurgeCalls);
        Assert.DoesNotContain("PRIVATE-", await unknown.Content.ReadAsStringAsync());
    }
    [TestMethod]
    public async Task JobReferenceCannotBeReadThroughAnotherTenant()
    {
        await using var f = await Fixture.Create(); using var preview = await f.Preflight(); using var confirmed = await f.Submit();
        var foreign = confirmed.Headers.Location!.ToString().Replace(f.Target.Tenant.ToString("D"), Guid.NewGuid().ToString("D"));
        var writes = f.Writes; using var response = await f.Http.GetAsync(foreign);
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode); Assert.AreEqual("unknown", (await Data(response)).Outcome);
        Assert.AreEqual(writes, f.Writes); Assert.AreEqual(0, f.Target.Native.Native.PurgeCalls);
    }
    private static async Task<ManagementTenantPurgeResult> Data(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").Deserialize<ManagementTenantPurgeResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    private sealed class Fixture : IAsyncDisposable
    {
        public PurgePortalTargetFixture Target { get; private set; } = null!;
        public WebApplication Portal { get; private set; } = null!; public HttpClient Http { get; private set; } = null!;
        public ManagementApplicationEntity Application { get; set; } = null!;
        public ManagementApplicationAccessEntity Assignment { get; set; } = null!;
        public ConcurrentBag<ManagementAuditEntity> Audits { get; } = [];
        public bool EnabledUser = true; public int SecretReads, Writes;
        public Func<HttpRequestMessage, HttpResponseMessage, Task<HttpResponseMessage>>? AfterSend;
        public string Path => $"/api/management/applications/{Target.AppId:D}/tenants/{Target.Tenant:D}/purges";
        public static async Task<Fixture> Create(bool install = true)
        {
            var f = new Fixture { Target = await PurgePortalTargetFixture.Create() };
            var registry = new ManagementOperationRegistry([ManagementTenantPurgeOperation.Descriptor]);
            var connectionId = Guid.NewGuid();
            f.Application = new() { OrganizationId = f.Target.Org, ApplicationId = f.Target.AppId, ApplicationKey = "fixture", Environment = "Testing", BaseUrl = "https://fixture.example", ActiveCredentialId = connectionId, Version = "app-1" };
            f.Assignment = new() { OrganizationId = f.Target.Org, ApplicationId = f.Target.AppId, UserId = f.Target.User, Permissions = [ManagementTenantDataOperations.Delete], Version = "assignment-1" };
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", f.Target.User.ToString("D")), new(ManagementClaimTypes.SubjectType, ManagementSubjectTypes.Human),
                new(ManagementClaimTypes.OrganizationId, f.Target.Org.ToString("D")), new(ManagementClaimTypes.Permission, ManagementOperations.IdentityRead)], "trusted-support"));
            var principals = new Mock<IManagementPrincipalProvider>(); principals.Setup(x => x.GetPrincipal()).Returns(principal);
            var apps = new Mock<IManagementApplicationStateProvider>(); apps.Setup(x => x.GetAsync(f.Target.Org, f.Target.AppId, It.IsAny<CancellationToken>())).ReturnsAsync(() => f.Application);
            var assignments = new Mock<IManagementApplicationAccessRepository>(); assignments.Setup(x => x.GetAsync(f.Target.Org, f.Target.AppId, f.Target.User, It.IsAny<CancellationToken>())).ReturnsAsync(() => f.Assignment);
            var users = new Mock<IManagementSupportUserStateProvider>(); users.Setup(x => x.GetAsync(f.Target.Org, f.Target.User, It.IsAny<CancellationToken>())).ReturnsAsync(() => new(f.Target.Org, f.Target.User, f.EnabledUser, false));
            var info = new ManagementRegisteredOperationInfo("fixture", "Testing", f.Target.Realm, "dedicated-app", 1, registry.Capabilities([ManagementTenantDataOperations.Delete], [ManagementTenantDataOperations.Delete], _ => true).ToArray());
            var connection = new ManagementApplicationCredentialEntity { OrganizationId = f.Target.Org, ApplicationId = f.Target.AppId, CredentialId = connectionId, BaseUrl = f.Application.BaseUrl,
                SecretReference = "private-reference", Capabilities = [ManagementRegisteredOperations.Info, ManagementTenantDataOperations.Delete], Operations = new("available", info) };
            var connections = new Mock<IManagementApplicationCredentialRepository>(); connections.Setup(x => x.GetAsync(f.Target.Org, f.Target.AppId, connectionId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
            var secrets = new Mock<IManagementSecretStore>(); secrets.Setup(x => x.ReadAsync(new(f.Target.Org, f.Target.AppId), "private-reference", It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => { f.SecretReads++; return new(f.Target.Secret); });
            var audit = new Mock<IManagementAuditRepository>();
            audit.Setup(x => x.AppendAsync(It.IsAny<ManagementAuditEntity>(), It.IsAny<CancellationToken>())).Callback<ManagementAuditEntity, CancellationToken>((e, _) => f.Audits.Add(e)).Returns(Task.CompletedTask);
            var executor = new ServiceOperationExecutor(auditTrailSink: new ManagementAuditSink(audit.Object, principals.Object, new ManagementAuditContext(),
                Options.Create(new ManagementAuditOptions()), Options.Create(new ManagementDiagnosticsOptions()), TimeProvider.System, registry), auditOptionsMonitor: new Monitor<ServiceAuditOptions>(new() { Enabled = true, FailOnAuditError = true }));
            var access = new ManagementApplicationAccessService(assignments.Object, apps.Object, users.Object, principals.Object, executor, registry);
            var available = new ManagementRegisteredOperationAvailabilityService(registry, principals.Object, apps.Object, connections.Object, access, executor);
            var signing = new Mock<IManagementActorSigningKeyProvider>(); signing.Setup(x => x.GetSigningCredentials()).Returns(() => f.Target.Signing);
            var options = new Monitor<ManagementRegisteredOperationClientOptions>(new() { Issuer = "fixture-portal", OperationIdKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), Targets = [new() { ApplicationId = f.Target.AppId, Audience = "fixture-target" }] });
            var networkOptions = Options.Create(new ManagementConnectionOptions { ApprovedOrigins = [f.Application.BaseUrl] });
            var network = new ManagementConnectionNetworkPolicy(networkOptions, Mock.Of<IManagementTargetDnsResolver>(), Mock.Of<IManagementTargetSocketConnector>());
            var client = new ManagementRegisteredOperationClient(registry, principals.Object, access, available, apps.Object, connections.Object, secrets.Object, new ManagementCredentialSecretCodec(),
                signing.Object, new ManagementAuditContext(), network, networkOptions, options, f.Target.Native.Clock, executor)
                { TestTransportFactory = () => new Bridge(f) { InnerHandler = f.Target.Server.GetTestServer().CreateHandler() } };
            var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["IBeam:Management:Api:AuthenticationScheme"] = "support" });
            builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, Auth>("support", _ => { });
            builder.Services.AddSingleton<IServiceOperationExecutor>(executor);
            builder.Services.AddSingleton<IManagementOperationClient>(client);
            if (install) builder.Services.AddIBeamManagementPortalTenantPurgeApi(builder.Configuration);
            else builder.Services.AddIBeamManagementApi(builder.Configuration);
            f.Portal = builder.Build(); f.Portal.UseAuthentication(); f.Portal.UseAuthorization(); f.Portal.MapControllers();
            await f.Portal.StartAsync(); f.Http = f.Portal.GetTestClient(); f.Http.DefaultRequestHeaders.Authorization = new("Bearer", "support"); return f;
        }
        public async Task<HttpResponseMessage> Submit(object? body = null, string version = "v1", string? key = "portal-purge-intent")
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = JsonContent.Create(body ?? new { policyKey = "enterprise", planId = Plan!.PlanId, confirmationBinding = Plan.ConfirmationBinding }) };
            request.Headers.TryAddWithoutValidation("If-Match", version); if (key is not null) request.Headers.Add("Idempotency-Key", key);
            return await Http.SendAsync(request);
        }
        public ManagementTenantPurgePlan? Plan;
        public async Task<HttpResponseMessage> Preflight(string key = "portal-purge-plan") {
            using var request = new HttpRequestMessage(HttpMethod.Post, Path.Replace("/purges", "/purge-plans")) { Content = JsonContent.Create(new { policyKey = "enterprise" }) };
            request.Headers.TryAddWithoutValidation("If-Match", "v1"); request.Headers.Add("Idempotency-Key", key);
            var response = await Http.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.OK) Plan = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("plan").Deserialize<ManagementTenantPurgePlan>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return response;
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
