using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Models;
using IBeam.Identity.Repositories.AzureTable.Stores;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Entities;
using IBeam.Management.Identity.Target.Services.Invitations;
using IBeam.Management.Operations;
using IBeam.Management.Operations.Target.Api;
using IBeam.Management.Repositories.AzureTable;
using IBeam.Management.Services;
using IBeam.Management.Services.Credentials;
using IBeam.Management.Services.CustomerIdentity.Delegation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace IBeam.Tests.Management.TenantData;

[TestClass, TestCategory("StorageIntegration")]
public sealed class BulkInvitationHttpTests
{
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task RealSignedBulkBoundaryUsesNativeCasAndReadonlyOriginalReceipt(bool lostReply)
    {
        await using var f = await Fixture.Create(autoBridge: !lostReply); f.Transitions.LoseReply = lostReply;
        using var command = f.Request(); using var response = await f.Http.SendAsync(command); Assert.AreEqual(lostReply ? HttpStatusCode.Accepted : HttpStatusCode.OK, response.StatusCode);
        var first = await response.Content.ReadFromJsonAsync<JsonElement>(); var original = first.GetProperty("data").Deserialize<ManagementBulkInvitationRevokeResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.AreEqual(f.Scope.OperationId, original.OperationId); Assert.AreEqual(f.Native.Tenant, original.TenantId);
        Assert.AreEqual(lostReply ? "unknown" : "succeeded", original.State); var version = (await f.Invites.GetAsync(f.Native.Tenant, f.Native.Invite))!.Version;
        var calls = f.Transitions.Calls; using var poll = f.Request(receipt: true); using var status = await f.Http.SendAsync(poll); Assert.AreEqual(HttpStatusCode.OK, status.StatusCode); Assert.AreEqual(calls, f.Transitions.Calls);
        using var resume = f.Request(); using var resumed = await f.Http.SendAsync(resume); Assert.AreEqual(HttpStatusCode.OK, resumed.StatusCode);
        Assert.AreEqual("succeeded", (await resumed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("state").GetString());
        Assert.AreEqual(version, (await f.Invites.GetAsync(f.Native.Tenant, f.Native.Invite))!.Version);
        using var replay = f.Request(assertion: f.OriginalAssertion); using var replayed = await f.Http.SendAsync(replay); Assert.AreEqual(HttpStatusCode.Forbidden, replayed.StatusCode);
        Assert.DoesNotContain("PRIVATE-", first.GetRawText()); Assert.DoesNotContain(f.Secret, first.GetRawText());
        Assert.IsTrue(f.Audits.Any(x => x.Operation == ManagementBulkInvitationRevokeOperation.Name && x.OrganizationId == f.Scope.OrganizationId && x.ActorUserId == f.Scope.ActorUserId));
        Assert.DoesNotContain("PRIVATE-", JsonSerializer.Serialize(f.Audits)); Assert.DoesNotContain(f.Secret, JsonSerializer.Serialize(f.Audits));
    }
    [TestMethod]
    [DataRow("borrowed-grant")] [DataRow("realm")] [DataRow("app")] [DataRow("body")] [DataRow("version")]
    public async Task BulkBoundaryRejectsBorrowedAuthorityAndAlteredBindingsBeforeMutation(string change)
    {
        await using var f = await Fixture.Create();
        if (change == "borrowed-grant") f.Credential = f.Credential with { Capabilities = [ManagementRegisteredOperations.Info, ManagementCustomerIdentityOperations.InvitesManage] };
        using var request = f.Request(change: change); using var response = await f.Http.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode); Assert.AreEqual(0, f.Transitions.Calls);
        Assert.AreEqual(TenantInviteStatuses.Pending, (await f.Invites.GetAsync(f.Native.Tenant, f.Native.Invite))!.Status);
    }
    [TestMethod]
    public void BulkRegistrationIsOptionalAndItsGrantDoesNotDependOnInvitesManage()
    {
        var services = new ServiceCollection(); services.AddIBeamManagementBulkInvitationRevokeTarget(new ConfigurationBuilder().Build(), _ => { }); Assert.HasCount(0, services);
        var registry = new ManagementOperationRegistry([ManagementBulkInvitationRevokeOperation.Descriptor]);
        CollectionAssert.AreEqual(new[] { ManagementBulkInvitationRevokeOperation.Name }, registry.RequiredFor(ManagementBulkInvitationRevokeOperation.Name).ToArray());
        Assert.IsFalse(ManagementOperationRegistry.BuiltIn.Contains(ManagementBulkInvitationRevokeOperation.Name));
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public IdentityPurgeFixture Native = null!; public AzureTableTenantInviteStore Invites = null!; public ManagedTransition Transitions = null!;
        public ManagementOperationContext Scope = null!; public ManagementApiCredentialEntity Credential = null!; public string Secret = "", OriginalAssertion = "";
        public WebApplication Target = null!; public HttpClient Http = null!; public ConcurrentBag<ManagementAuditEntity> Audits { get; } = [];
        private readonly RSA rsa = RSA.Create(2048); private IServiceScope nativeServices = null!; private Azure.Data.Tables.TableClient table = null!;
        private byte[] body = []; private Guid credentialId;
        public static async Task<Fixture> Create(bool autoBridge = false)
        {
            var f = new Fixture { Native = await IdentityPurgeFixture.Create() }; f.nativeServices = f.Native.Provider.CreateScope(); var p = f.nativeServices.ServiceProvider;
            f.Invites = p.GetRequiredService<AzureTableTenantInviteStore>(); f.Transitions = new(f.Invites); f.table = f.Native.Table("HttpBulkInvitations"); await f.table.CreateIfNotExistsAsync();
            var tenantVersion = (await p.GetRequiredService<AzureTableIdentityTenantStore>().FindVersionedAsync(f.Native.Tenant))!.Version;
            f.Scope = f.Native.Execution.Authority with { OperationId = Guid.NewGuid(), ResourceId = Guid.NewGuid(), ResourceVersion = tenantVersion };
            var selected = new ManagementBulkInvitationRevokeRequest([new(f.Native.Invite, (await f.Invites.GetAsync(f.Native.Tenant, f.Native.Invite))!.Version!)]);
            f.body = new ManagementOperationPayloadCodec(new([ManagementBulkInvitationRevokeOperation.Descriptor])).WriteRequest(ManagementBulkInvitationRevokeOperation.Name, selected);
            f.credentialId = Guid.NewGuid(); var codec = new ManagementCredentialSecretCodec(); f.Secret = codec.Create(f.credentialId);
            f.Credential = new() { CredentialId = f.credentialId, ApplicationKey = "fixture", Environment = "Testing", SecretHash = codec.Hash(f.Secret),
                Capabilities = [ManagementRegisteredOperations.Info, ManagementBulkInvitationRevokeOperation.Name], ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1), Version = "credential-1" };
            var keys = new Mock<IManagementActorValidationKeyProvider>(); keys.Setup(x => x.GetTrustedKeys("fixture-portal")).Returns([new RsaSecurityKey(f.rsa) { KeyId = "fixture-signing" }]);
            var credentials = new Mock<IManagementApiCredentialRepository>(); credentials.Setup(x => x.GetAsync("fixture", "Testing", f.credentialId, It.IsAny<CancellationToken>())).ReturnsAsync(() => f.Credential);
            var ids = new ConcurrentDictionary<Guid, byte>(); var replays = new Mock<IManagementDelegationReplayStore>(); replays.Setup(x => x.TryClaimAsync(f.Scope.RealmId, "fixture-portal", It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, string _, Guid id, DateTimeOffset _, CancellationToken _) => ids.TryAdd(id, 0));
            var audit = new Mock<IManagementAuditRepository>(); audit.Setup(x => x.AppendAsync(It.IsAny<ManagementAuditEntity>(), It.IsAny<CancellationToken>())).Callback<ManagementAuditEntity, CancellationToken>((row, _) => f.Audits.Add(row)).Returns(Task.CompletedTask);
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" }); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
                ["IBeam:Management:Diagnostics:ApplicationKey"] = "fixture", ["IBeam:Management:Diagnostics:Environment"] = "Testing",
                ["IBeam:Management:CustomerIdentity:IdentityRealmId"] = f.Scope.RealmId.ToString("D"), ["IBeam:Management:CustomerIdentity:DedicatedApplicationRealm"] = "true",
                ["IBeam:Management:Operations:Target:RealmId"] = f.Scope.RealmId.ToString("D"), ["IBeam:Management:Operations:Target:DedicatedApplicationRealm"] = "true",
                ["IBeam:Management:Operations:Target:EnabledOperations:0"] = ManagementBulkInvitationRevokeOperation.Name,
                ["IBeam:Management:Operations:Target:TrustedPortals:0:OrganizationId"] = f.Scope.OrganizationId.ToString("D"), ["IBeam:Management:Operations:Target:TrustedPortals:0:ApplicationId"] = f.Scope.ApplicationId.ToString("D"),
                ["IBeam:Management:Operations:Target:TrustedPortals:0:Issuer"] = "fixture-portal", ["IBeam:Management:Operations:Target:TrustedPortals:0:Audience"] = "fixture-target" });
            builder.Services.AddSingleton(credentials.Object); builder.Services.AddSingleton(keys.Object); builder.Services.AddSingleton(replays.Object); builder.Services.AddSingleton(audit.Object);
            builder.Services.AddSingleton<ITenantInviteStore>(f.Invites); if (!autoBridge) builder.Services.AddSingleton<IManagedTenantInviteStore>(f.Transitions);
            builder.Services.AddSingleton<IIdentityTenantStore>(p.GetRequiredService<AzureTableIdentityTenantStore>());
            builder.Services.AddIBeamManagementAzureTableBulkInvitationBatchStore(f.table); builder.Services.AddIBeamManagementBulkInvitationRevokeTarget(builder.Configuration, o => o.Enabled = true);
            builder.Services.AddIBeamManagementRegisteredOperationsTargetApi(builder.Configuration);
            f.Target = builder.Build(); f.Target.UseAuthentication(); f.Target.UseAuthorization(); f.Target.MapIBeamManagementRegisteredOperations(); await f.Target.StartAsync(); f.Http = f.Target.GetTestClient(); return f;
        }
        public HttpRequestMessage Request(bool receipt = false, string? assertion = null, string? change = null)
        {
            var operation = ManagementBulkInvitationRevokeOperation.Name; var path = receipt ? "/api/management-operations/receipts/" + Scope.OperationId.ToString("D") : ManagementRegisteredRoute.Path(ManagementBulkInvitationRevokeOperation.Descriptor, Scope.TenantId, Scope.ResourceId);
            var wire = new ManagementDelegationRequest { Method = receipt ? "GET" : "POST", PathAndQuery = path, Body = receipt ? [] : body, IdempotencyKey = receipt ? null : "bulk-original-intent", IfMatch = receipt ? null : Scope.ResourceVersion };
            var digest = receipt ? ManagementDelegationBinding.HashRegisteredReceipt(wire) : ManagementDelegationBinding.Hash(wire, operation, new([ManagementBulkInvitationRevokeOperation.Descriptor]));
            if (assertion is null) {
                var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                var jwt = new JwtSecurityToken("fixture-portal", "fixture-target", [new("sub", Scope.ActorUserId.ToString("D")), new("org", Scope.OrganizationId.ToString("D")),
                    new("app", (change == "app" ? Guid.NewGuid() : Scope.ApplicationId).ToString("D")), new("realm", (change == "realm" ? Guid.NewGuid() : Scope.RealmId).ToString("D")),
                    new("credential", credentialId.ToString("D")), new("application_key", "fixture"), new("environment", "Testing"), new("operation", operation), new("binding", digest),
                    new("tenant", Scope.TenantId.ToString("D")), new("resource", Scope.ResourceId.ToString("D")), new("operation_id", Scope.OperationId.ToString("D")),
                    new("jti", Guid.NewGuid().ToString("D")), new("correlation", "0123456789abcdef0123456789abcdef"), new("iat", now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)],
                    now.UtcDateTime, now.AddSeconds(30).UtcDateTime, new SigningCredentials(new RsaSecurityKey(rsa) { KeyId = "fixture-signing" }, SecurityAlgorithms.RsaSha256));
                jwt.Header["typ"] = ManagementRegisteredTargetAuthority.TokenType; assertion = new JwtSecurityTokenHandler().WriteToken(jwt); if (!receipt && OriginalAssertion.Length == 0) OriginalAssertion = assertion;
            }
            var request = new HttpRequestMessage(receipt ? HttpMethod.Get : HttpMethod.Post, path); request.Headers.Add("X-IBeam-Management-Key", Secret); request.Headers.Add("X-IBeam-Management-Actor", assertion);
            if (!receipt) { request.Content = new ByteArrayContent(change == "body" ? Encoding.UTF8.GetBytes("{\"invitations\":[]}") : body); request.Content.Headers.ContentType = new("application/json"); request.Headers.Add("Idempotency-Key", "bulk-original-intent"); request.Headers.TryAddWithoutValidation("If-Match", change == "version" ? "altered" : Scope.ResourceVersion); }
            return request;
        }
        public async ValueTask DisposeAsync() { Http.Dispose(); await Target.DisposeAsync(); nativeServices.Dispose(); if (!table.Uri.IsLoopback || !table.Name.EndsWith("HttpBulkInvitations", StringComparison.Ordinal)) throw new InvalidOperationException(); await table.DeleteAsync(); await Native.DisposeAsync(); rsa.Dispose(); }
    }
    private sealed class ManagedTransition(IManagedTenantInviteStore inner) : IManagedTenantInviteStore
    {
        public int Calls; public bool LoseReply;
        public Task<TenantInviteRecord> BeginAcceptanceAsync(Guid tenant, Guid invite, string hash, Guid user, CancellationToken ct = default) => inner.BeginAcceptanceAsync(tenant, invite, hash, user, ct);
        public async Task<TenantInviteRecord> TransitionAsync(ManagedTenantInviteTransition request, CancellationToken ct = default) { Calls++; var row = await inner.TransitionAsync(request, ct); if (LoseReply) { LoseReply = false; throw new IOException("PRIVATE-NATIVE-REPLY"); } return row; }
    }
}
