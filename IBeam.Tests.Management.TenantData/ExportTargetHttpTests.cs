using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Entities;
using IBeam.Management.Operations;
using IBeam.Management.Operations.Target.Api;
using IBeam.Management.Services;
using IBeam.Management.Services.Credentials;
using IBeam.Management.Services.CustomerIdentity.Delegation;
using IBeam.Management.TenantData;
using IBeam.Management.TenantData.Target.Api;
using IBeam.Management.TenantData.Target.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace IBeam.Tests.Management.TenantData;

[TestClass]
public sealed class ExportTargetHttpTests
{
    [TestMethod]
    public async Task StandardDurableJournalAndWorkerIntegrateWithTheRealSignedHttpBoundary()
    {
        await using var f = await Fixture.Create(standardJobs: true);
        using var submit = f.Request(); using var response = await f.Http.SendAsync(submit);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("requested", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("job").GetProperty("state").GetString());
        Assert.AreEqual(1, f.Journal.Count);
        var execution = (await f.Journal.ListPageAsync(new("fixture", "Testing", f.Org, f.AppId, f.Realm), 1, null)).Items.Single();
        using (var scope = f.Server.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IManagementTenantExportWorker>().RunAsync(execution);
        using var poll = f.Request(lookup: true); using var status = await f.Http.SendAsync(poll);
        Assert.AreEqual(HttpStatusCode.OK, status.StatusCode);
        Assert.AreEqual("succeeded", (await status.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("job").GetProperty("state").GetString());
        using var replay = f.Request(); using var repeated = await f.Http.SendAsync(replay);
        Assert.AreEqual(HttpStatusCode.OK, repeated.StatusCode); Assert.AreEqual(1, f.Journal.Count);
        Assert.DoesNotContain("PRIVATE-", JsonSerializer.Serialize(f.Audits));
    }
    [TestMethod]
    public async Task SignedNativeSubmitAndReadonlyStatusExposeOnlySafeRequestedJobMetadata()
    {
        await using var f = await Fixture.Create();
        using var request = f.Request(); using var response = await f.Http.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("no-store", response.Headers.CacheControl!.ToString());
        var body = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.AreEqual("accepted", body.GetProperty("outcome").GetString());
        Assert.AreEqual("requested", body.GetProperty("job").GetProperty("state").GetString());
        Assert.HasCount(3, body.GetProperty("job").GetProperty("coverage").EnumerateArray());
        using var poll = f.Request(lookup: true); using var found = await f.Http.SendAsync(poll);
        Assert.AreEqual(HttpStatusCode.OK, found.StatusCode); Assert.AreEqual(1, f.Submits); Assert.AreEqual(1, f.Lookups);
        Assert.AreEqual("found", (await found.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("outcome").GetString());
        Assert.DoesNotContain("PRIVATE-", JsonSerializer.Serialize(f.Audits));
    }
    [TestMethod]
    [DataRow("key-only")] [DataRow("borrowed-grant")] [DataRow("scope")]
    [DataRow("body")] [DataRow("version")] [DataRow("policy")]
    public async Task UnauthorizedOrSubstitutedRequestsCannotSubmitExportJobs(string change)
    {
        await using var f = await Fixture.Create(); using var request = f.Request();
        switch (change)
        {
            case "key-only": request.Headers.Remove("X-IBeam-Management-Actor"); break;
            case "borrowed-grant": f.Credential = f.Credential with { Capabilities = [ManagementRegisteredOperations.Info, ManagementCustomerIdentityOperations.TenantsStatus] }; break;
            case "scope": request.RequestUri = new Uri(request.RequestUri!.ToString().Replace(f.Tenant.ToString("D"), Guid.NewGuid().ToString("D")), UriKind.Relative); break;
            case "body": request.Content = new StringContent("{\"policyKey\":\"PRIVATE-DESTINATION\",\"expectedVersion\":\"tenant-1\"}", Encoding.UTF8, "application/json"); break;
            case "version": request.Headers.Remove("If-Match"); request.Headers.TryAddWithoutValidation("If-Match", "tenant-2"); break;
            case "policy": f.PolicyDenied = true; break;
        }
        using var response = await f.Http.SendAsync(request);
        Assert.IsTrue(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest);
        Assert.AreEqual(0, f.Submits); Assert.DoesNotContain("PRIVATE-", await response.Content.ReadAsStringAsync());
    }
    [TestMethod]
    public async Task ReceiptAliasIsBoundToSignedJobAndTenantAndRawWriteProofCannotReplay()
    {
        await using var f = await Fixture.Create();
        using var original = f.Request(); var assertion = original.Headers.GetValues("X-IBeam-Management-Actor").Single();
        using var first = await f.Http.SendAsync(original); Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        using var replay = f.Request(); replay.Headers.Remove("X-IBeam-Management-Actor"); replay.Headers.Add("X-IBeam-Management-Actor", assertion);
        using var rejected = await f.Http.SendAsync(replay); Assert.AreEqual(HttpStatusCode.Forbidden, rejected.StatusCode);
        using var poll = f.Request(lookup: true); poll.RequestUri = new Uri(poll.RequestUri!.ToString().Replace(f.Job.ToString("D"), Guid.NewGuid().ToString("D")), UriKind.Relative);
        using var denied = await f.Http.SendAsync(poll); Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.AreEqual(1, f.Submits); Assert.AreEqual(0, f.Lookups);
    }
    [TestMethod]
    public async Task ProviderErrorsAndUncertainJobRepliesNeverReturnBusinessTextOrSuccessMetadata()
    {
        await using var f = await Fixture.Create(); f.ProviderError = true;
        using var request = f.Request(); using var failed = await f.Http.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.DoesNotContain("PRIVATE-", await failed.Content.ReadAsStringAsync());
        f.ProviderError = false; f.Unknown = true;
        using var next = f.Request(); using var uncertain = await f.Http.SendAsync(next);
        Assert.AreEqual(HttpStatusCode.Accepted, uncertain.StatusCode);
        var result = (await uncertain.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.AreEqual("unknown", result.GetProperty("outcome").GetString()); Assert.AreEqual(JsonValueKind.Null, result.GetProperty("job").ValueKind);
        Assert.DoesNotContain("PRIVATE-", JsonSerializer.Serialize(f.Audits));
        Assert.IsTrue(f.Audits.Any(x => x.Operation == ManagementTenantDataOperations.Export && !x.Succeeded));
    }
    internal sealed class Fixture : IAsyncDisposable
    {
        public Guid Tenant { get; } = Guid.NewGuid(); public Guid Job { get; } = Guid.NewGuid();
        public Guid Org { get; } = Guid.NewGuid(); public Guid AppId { get; } = Guid.NewGuid(); public Guid Realm { get; } = Guid.NewGuid(); public Guid User { get; } = Guid.NewGuid();
        public ManagementApiCredentialEntity Credential { get; set; } = null!;
        public int Submits; public int Lookups; public bool PolicyDenied; public bool ProviderError; public bool Unknown;
        public ConcurrentBag<ManagementAuditEntity> Audits { get; } = [];
        public DurableExportTests.Journal Journal { get; } = new();
        public WebApplication Server { get; private set; } = null!; public HttpClient Http { get; private set; } = null!;
        private readonly RSA rsa = RSA.Create(2048); private string secret = "";
        internal string Secret => secret;
        internal SigningCredentials Signing => new(new RsaSecurityKey(rsa) { KeyId = "export-fixture-key" }, SecurityAlgorithms.RsaSha256);
        public static async Task<Fixture> Create(bool standardJobs = false)
        {
            var f = new Fixture(); var codec = new ManagementCredentialSecretCodec(); var credentialId = Guid.NewGuid(); f.secret = codec.Create(credentialId);
            f.Credential = new() { CredentialId = credentialId, ApplicationKey = "fixture", Environment = "Testing", Version = "credential-1", SecretHash = codec.Hash(f.secret),
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1), Capabilities = [ManagementRegisteredOperations.Info, ManagementTenantDataOperations.Export] };
            var key = new RsaSecurityKey(f.rsa) { KeyId = "export-fixture-key" };
            var keys = new Mock<IManagementActorValidationKeyProvider>(); keys.Setup(x => x.GetTrustedKeys("fixture-portal")).Returns([key]);
            var credentials = new Mock<IManagementApiCredentialRepository>(); credentials.Setup(x => x.GetAsync("fixture", "Testing", credentialId, It.IsAny<CancellationToken>())).ReturnsAsync(() => f.Credential);
            var audit = new Mock<IManagementAuditRepository>(); audit.Setup(x => x.AppendAsync(It.IsAny<ManagementAuditEntity>(), It.IsAny<CancellationToken>()))
                .Callback<ManagementAuditEntity, CancellationToken>((entry, _) => f.Audits.Add(entry)).Returns(Task.CompletedTask);
            var replayIds = new ConcurrentDictionary<Guid, byte>(); var replay = new Mock<IManagementDelegationReplayStore>();
            replay.Setup(x => x.TryClaimAsync(f.Realm, "fixture-portal", It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, string _, Guid id, DateTimeOffset _, CancellationToken _) => replayIds.TryAdd(id, 0));
            var handler = new Mock<IManagementTenantExportHandler>(); handler.SetupGet(x => x.ProtocolVersion).Returns(1); handler.Setup(x => x.Describe()).Returns(ExportContractTests.Definition);
            handler.Setup(x => x.ExecuteOrResumeAsync(It.IsAny<ManagementTenantExportExecution>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ManagementTenantExportExecution e, CancellationToken _) => new ManagementTenantExportOutput(new("PRIVATE-STANDARD-ARTIFACT", e.ArtifactExpiresUtc!.Value),
                    ExportContractTests.Definition.Datasets.Select(x => new ManagementTenantExportCoverage(x.Key, x.Kind, "completed")).ToArray()));
            var delivery = new Mock<IManagementTenantExportDelivery>(); delivery.SetupGet(x => x.ProtocolVersion).Returns(1);
            var customer = new Mock<IManagementTenantExportCustomerAuthorization>(); customer.SetupGet(x => x.ProtocolVersion).Returns(1);
            var policy = new Mock<IManagementTenantExportPolicy>(); policy.SetupGet(x => x.ProtocolVersion).Returns(1);
            policy.Setup(x => x.RequireAsync(It.IsAny<ManagementTenantExportExecution>(), "tenant-1", It.IsAny<CancellationToken>()))
                .Returns(() => f.PolicyDenied ? Task.FromException(new ManagementAccessDeniedException()) : Task.CompletedTask);
            var jobs = new Mock<IManagementTenantExportJobService>(); jobs.SetupGet(x => x.ProtocolVersion).Returns(1); jobs.SetupGet(x => x.Durable).Returns(false);
            jobs.Setup(x => x.SubmitAsync(It.IsAny<ManagementTenantExportExecution>(), It.IsAny<ManagementTenantExportRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ManagementTenantExportExecution execution, ManagementTenantExportRequest _, CancellationToken _) => {
                    f.Submits++; if (f.ProviderError) throw new InvalidOperationException("PRIVATE-PROVIDER-TEXT");
                    return f.Unknown ? new(execution.Authority.TenantId, execution.JobId, "unknown", null) : ExportContractTests.Result(execution.Authority.TenantId, execution.JobId);
                });
            jobs.Setup(x => x.FindAsync(It.IsAny<ManagementTenantExportExecution>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ManagementTenantExportExecution execution, CancellationToken _) => { f.Lookups++; return ExportContractTests.Result(execution.Authority.TenantId, execution.JobId) with { Outcome = "found" }; });
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" }); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
                ["IBeam:Management:Diagnostics:ApplicationKey"] = "fixture", ["IBeam:Management:Diagnostics:Environment"] = "Testing",
                ["IBeam:Management:Operations:Target:RealmId"] = f.Realm.ToString("D"), ["IBeam:Management:Operations:Target:DedicatedApplicationRealm"] = "true",
                ["IBeam:Management:Operations:Target:EnabledOperations:0"] = ManagementTenantDataOperations.Export,
                ["IBeam:Management:Operations:Target:TrustedPortals:0:OrganizationId"] = f.Org.ToString("D"), ["IBeam:Management:Operations:Target:TrustedPortals:0:ApplicationId"] = f.AppId.ToString("D"),
                ["IBeam:Management:Operations:Target:TrustedPortals:0:Issuer"] = "fixture-portal", ["IBeam:Management:Operations:Target:TrustedPortals:0:Audience"] = "fixture-target" });
            builder.Services.AddSingleton(keys.Object); builder.Services.AddSingleton(credentials.Object); builder.Services.AddSingleton(replay.Object); builder.Services.AddSingleton(audit.Object);
            builder.Services.AddSingleton(handler.Object); builder.Services.AddSingleton(delivery.Object); builder.Services.AddSingleton(customer.Object); builder.Services.AddSingleton(policy.Object);
            if (standardJobs)
            {
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
                    ["IBeam:Management:TenantExport:Jobs:ActiveKeyId"] = "artifact.v1",
                    ["IBeam:Management:TenantExport:Jobs:ArtifactProtectionKeys:artifact.v1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) });
                builder.Services.AddSingleton<IManagementTenantExportJobRepository>(f.Journal);
                builder.Services.AddIBeamManagementTenantExportJobs(builder.Configuration);
            }
            else builder.Services.AddSingleton(jobs.Object);
            builder.Services.AddIBeamManagementTenantDataTargetApi(builder.Configuration, o => { o.Enabled = true; o.AllowVolatileDevelopmentJobs = true; o.PolicyKeys = ["enterprise"]; });
            f.Server = builder.Build(); f.Server.UseAuthentication(); f.Server.UseAuthorization(); f.Server.MapIBeamManagementRegisteredOperations();
            await f.Server.StartAsync(); f.Http = f.Server.GetTestClient(); return f;
        }
        public HttpRequestMessage Request(bool lookup = false)
        {
            var path = lookup ? ManagementTenantExportRoutes.StatusPath(Tenant, Job) : $"/api/management-tenant-data/tenants/{Tenant:D}/exports";
            var body = lookup ? [] : JsonSerializer.SerializeToUtf8Bytes(new ManagementTenantExportRequest("enterprise", "tenant-1"), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var wire = new ManagementDelegationRequest { Method = lookup ? "GET" : "POST", PathAndQuery = path, Body = body, IdempotencyKey = lookup ? null : "export-intent", IfMatch = lookup ? null : "tenant-1" };
            var registry = Server.Services.GetRequiredService<ManagementOperationRegistry>();
            var digest = lookup ? ManagementDelegationBinding.HashRegisteredReceipt(wire) : ManagementDelegationBinding.Hash(wire, ManagementTenantDataOperations.Export, registry);
            var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var token = new JwtSecurityToken("fixture-portal", "fixture-target", [new("sub", User.ToString("D")), new("org", Org.ToString("D")), new("app", AppId.ToString("D")),
                new("realm", Realm.ToString("D")), new("credential", Credential.CredentialId.ToString("D")), new("application_key", "fixture"), new("environment", "Testing"),
                new("operation", ManagementTenantDataOperations.Export), new("binding", digest), new("tenant", Tenant.ToString("D")), new("resource", Tenant.ToString("D")), new("operation_id", Job.ToString("D")),
                new("jti", Guid.NewGuid().ToString("D")), new("correlation", Guid.NewGuid().ToString("N")), new("iat", now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)],
                now.UtcDateTime, now.AddSeconds(30).UtcDateTime, new SigningCredentials(new RsaSecurityKey(rsa) { KeyId = "export-fixture-key" }, SecurityAlgorithms.RsaSha256));
            token.Header["typ"] = "ibeam-management-operation+jwt";
            var request = new HttpRequestMessage(lookup ? HttpMethod.Get : HttpMethod.Post, path);
            request.Headers.Add("X-IBeam-Management-Key", secret); request.Headers.Add("X-IBeam-Management-Actor", new JwtSecurityTokenHandler().WriteToken(token));
            if (!lookup) { request.Content = new ByteArrayContent(body); request.Content.Headers.ContentType = new("application/json"); request.Headers.Add("Idempotency-Key", "export-intent"); request.Headers.TryAddWithoutValidation("If-Match", "tenant-1"); }
            return request;
        }
        public async ValueTask DisposeAsync() { Http.Dispose(); await Server.DisposeAsync(); rsa.Dispose(); }
    }
}
