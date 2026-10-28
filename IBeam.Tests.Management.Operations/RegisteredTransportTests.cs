using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IBeam.Licensing;
using IBeam.Licensing.Services;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Entities;
using IBeam.Management.Licensing;
using IBeam.Management.Operations;
using IBeam.Management.Operations.Target.Api;
using IBeam.Management.Services;
using IBeam.Management.Services.Audit;
using IBeam.Management.Services.Credentials;
using IBeam.Management.Services.CustomerIdentity.Delegation;
using IBeam.Services.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace IBeam.Tests.Management.Operations;

[TestClass]
public sealed partial class RegisteredTransportTests
{
    [TestMethod]
    public async Task RealSignedClientDiscoversAndExecutesLicenceAndSeatHandlersWithReadonlyRecovery()
    {
        await using var f = await Fixture.Create();
        Assert.HasCount(6, (await f.Availability.GetAsync(f.AppId)).Info!.Capabilities);
        var initial = await f.Read();
        var suspended = await f.Suspend(initial.Version);
        Assert.AreEqual("suspended", suspended.Status);
        var receipt = await f.Client.ReadReceiptAsync<ManagementLicenseTransitionResult>(f.AppId, ManagementLicenseOperations.Suspend, f.Tenant, f.License, suspended.OperationId);
        Assert.AreEqual(suspended, receipt);
        var replay = await f.Suspend(initial.Version);
        Assert.AreEqual(suspended, replay);
        var restored = await f.Client.ExecuteAsync<ManagementLicenseRestoreRequest, ManagementLicenseTransitionResult>(f.AppId, ManagementLicenseOperations.Restore,
            f.Tenant, f.License, new(f.License, suspended.Version!), "restore-intent", suspended.Version);
        Assert.AreEqual("active", restored.Status);
        var seats = await f.Client.ExecuteAsync<ManagementSeatReadRequest, ManagementSeatStatus>(f.AppId, ManagementSeatOperations.Read, f.Tenant, f.License, new(), "", null);
        var user = Guid.NewGuid();
        var assigned = await f.Client.ExecuteAsync<ManagementSeatAssignRequest, ManagementSeatTransitionResult>(f.AppId, ManagementSeatOperations.Assign,
            f.Tenant, f.License, new(f.License, user, seats.Version), "assign-intent", seats.Version);
        Assert.AreEqual("succeeded", assigned.Outcome);
        var removed = await f.Client.ExecuteAsync<ManagementSeatUnassignRequest, ManagementSeatTransitionResult>(f.AppId, ManagementSeatOperations.Unassign,
            f.Tenant, f.License, new(f.License, user, assigned.AssignmentId!.Value, assigned.Version!), "unassign-intent", assigned.Version);
        Assert.AreEqual("succeeded", removed.Outcome);
        Assert.HasCount(0, (await f.Client.ExecuteAsync<ManagementSeatReadRequest, ManagementSeatStatus>(f.AppId, ManagementSeatOperations.Read, f.Tenant, f.License, new(), "", null)).Assignments);
        var audit = JsonSerializer.Serialize(f.Audits);
        Assert.DoesNotContain(f.Secret, audit);
        Assert.DoesNotContain(f.LastWrite!.Assertion, audit);
    }

    [TestMethod]
    [DataRow("borrowed-grants")] [DataRow("missing-read")] [DataRow("revoked-assignment")]
    [DataRow("disabled-user")] [DataRow("disabled-app")] [DataRow("missing-discovery")]
    [DataRow("wrong-realm")] [DataRow("fingerprint")]
    public async Task LivePortalAuthorityAndCompatibleMetadataAreRequiredBeforeReadingMachineSecrets(string change)
    {
        await using var f = await Fixture.Create();
        switch (change)
        {
            case "borrowed-grants": f.Assignment = f.Assignment with { Permissions = [ManagementCustomerIdentityOperations.TenantsRead, ManagementCustomerIdentityOperations.TenantsStatus] }; break;
            case "missing-read": f.Assignment = f.Assignment with { Permissions = [ManagementLicenseOperations.Suspend] }; break;
            case "revoked-assignment": f.Assignment = f.Assignment with { RevokedUtc = DateTimeOffset.UtcNow }; break;
            case "disabled-user": f.UserEnabled = false; break;
            case "disabled-app": f.Application = f.Application with { IsEnabled = false }; break;
            case "missing-discovery": f.Connection = f.Connection with { Operations = null }; break;
            case "wrong-realm": f.Connection = f.Connection with { Operations = f.Connection.Operations! with { Info = f.Connection.Operations.Info! with { RealmId = Guid.Empty } } }; break;
            case "fingerprint": f.Connection = f.Connection with { Operations = f.Connection.Operations! with { Info = f.Connection.Operations.Info! with {
                Capabilities = f.Connection.Operations.Info.Capabilities.Select(x => x with { DescriptorFingerprint = "tampered" }).ToArray() } } }; break;
        }
        try { await f.Suspend("original"); Assert.Fail("The operation must fail closed."); }
        catch (Exception ex) when (ex is ManagementAccessDeniedException or ManagementConnectionInvalidException) { }
        Assert.AreEqual(0, f.SecretReads); Assert.AreEqual(0, f.WriteCount);
        Assert.AreEqual("active", (await f.NativeRead()).License.Status);
    }

    [TestMethod]
    [DataRow("key-revoked")] [DataRow("machine-missing-read")] [DataRow("disabled-operation")]
    public async Task FreshTargetDiscoveryRejectsStaleConnectionGrantsBeforeMutation(string change)
    {
        await using var f = await Fixture.Create();
        if (change == "key-revoked") f.Credential = f.Credential with { RevokedUtc = DateTimeOffset.UtcNow };
        if (change == "machine-missing-read") f.Credential = f.Credential with { Capabilities = [ManagementRegisteredOperations.Info, ManagementLicenseOperations.Suspend] };
        if (change == "disabled-operation") f.Target.Services.GetRequiredService<IOptions<ManagementOperationTargetOptions>>().Value.EnabledOperations = [ManagementLicenseOperations.Read];
        await Assert.ThrowsAsync<ManagementOperationUnavailableException>(() => f.Suspend("original"));
        Assert.AreEqual(0, f.WriteCount); Assert.AreEqual("active", (await f.NativeRead()).License.Status);
    }

    [TestMethod]
    [DataRow("body")] [DataRow("path")] [DataRow("version")] [DataRow("idempotency")]
    [DataRow("assertion")] [DataRow("customer-bearer")]
    public async Task SignedWireSubstitutionIsRejectedWithoutChangingNativeState(string change)
    {
        await using var f = await Fixture.Create(); var version = (await f.Read()).Version;
        f.BeforeSend = request =>
        {
            if (request.Method != HttpMethod.Post) return;
            switch (change)
            {
                case "body": request.Content = new StringContent("{\"privateCanary\":\"PRIVATE-BODY\"}", Encoding.UTF8, "application/json"); break;
                case "path": request.RequestUri = new Uri(request.RequestUri!.AbsoluteUri.Replace(f.License.ToString("D"), Guid.NewGuid().ToString("D"))); break;
                case "version": request.Headers.Remove("If-Match"); request.Headers.TryAddWithoutValidation("If-Match", "changed"); break;
                case "idempotency": request.Headers.Remove("Idempotency-Key"); request.Headers.Add("Idempotency-Key", "changed-intent"); break;
                case "assertion": var assertion = request.Headers.GetValues("X-IBeam-Management-Actor").Single(); request.Headers.Remove("X-IBeam-Management-Actor"); request.Headers.Add("X-IBeam-Management-Actor", assertion[..^10] + "abcdefghij"); break;
                case "customer-bearer": request.Headers.Authorization = new("Bearer", "customer-token"); break;
            }
        };
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Suspend(version));
        Assert.AreEqual("active", (await f.NativeRead()).License.Status);
        Assert.DoesNotContain("PRIVATE-BODY", JsonSerializer.Serialize(f.Audits));
    }

    [TestMethod]
    public async Task CapturedWriteAssertionCannotBeReplayedEvenThoughSameIntentWithFreshProofIsSafe()
    {
        await using var f = await Fixture.Create(); var version = (await f.Read()).Version;
        var first = await f.Suspend(version);
        using var raw = f.Target.GetTestClient(); using var captured = f.LastWrite!.Request(f.Secret);
        using var denied = await raw.SendAsync(captured);
        Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.AreEqual(first, await f.Suspend(version));
    }

    [TestMethod]
    [DataRow("org")] [DataRow("app")] [DataRow("realm")] [DataRow("tenant")]
    [DataRow("resource")] [DataRow("credential")] [DataRow("aud")]
    [DataRow("iss")] [DataRow("typ")] [DataRow("expired")]
    public async Task EvenCorrectlySignedProofsCannotCrossScopeIssuerAudienceTypeOrLifetime(string change)
    {
        await using var f = await Fixture.Create(); var version = (await f.Read()).Version;
        f.BeforeSend = request =>
        {
            if (request.Method != HttpMethod.Post) return;
            var original = request.Headers.GetValues("X-IBeam-Management-Actor").Single();
            request.Headers.Remove("X-IBeam-Management-Actor");
            request.Headers.Add("X-IBeam-Management-Actor", f.ChangeProof(original, change));
        };
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Suspend(version));
        Assert.AreEqual("active", (await f.NativeRead()).License.Status);
    }

    [TestMethod]
    [DataRow("human")] [DataRow("machine")]
    public async Task RevocationBetweenFreshProbeAndWriteIsRecheckedAtBothBoundaries(string change)
    {
        await using var f = await Fixture.Create(); var version = (await f.Read()).Version;
        f.AfterSend = (request, response) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/management-operations/info")
            {
                if (change == "human") f.UserEnabled = false;
                else f.Credential = f.Credential with { RevokedUtc = DateTimeOffset.UtcNow };
            }
            return Task.FromResult(response);
        };
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Suspend(version));
        Assert.AreEqual(change == "human" ? 0 : 1, f.WriteCount);
        Assert.AreEqual("active", (await f.NativeRead()).License.Status);
    }

    [TestMethod]
    public async Task ReceiptLookupRequiresTheOriginalIndependentPermissionAndNeverMutates()
    {
        await using var f = await Fixture.Create(); var first = await f.Suspend((await f.Read()).Version);
        f.Assignment = f.Assignment with { Permissions = [ManagementLicenseOperations.Read] };
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Client.ReadReceiptAsync<ManagementLicenseTransitionResult>(f.AppId,
            ManagementLicenseOperations.Suspend, f.Tenant, f.License, first.OperationId));
        Assert.AreEqual(1, f.WriteCount);
    }

    [TestMethod]
    public async Task OptInConnectionProbeSupportsRegisteredOnlyTargetsWithoutChangingLegacyIdentityMetadata()
    {
        await using var f = await Fixture.Create();
        var options = Options.Create(new ManagementConnectionOptions { ApprovedOrigins = [f.Application.BaseUrl], EnableRegisteredOperationsDiscovery = true });
        var network = new ManagementConnectionNetworkPolicy(options, Mock.Of<IManagementTargetDnsResolver>(), Mock.Of<IManagementTargetSocketConnector>());
        var registry = f.Target.Services.GetRequiredService<ManagementOperationRegistry>();
        var probe = new ManagementTargetConnectionProbe(network, options, registry) { TestTransportFactory = () => f.Target.GetTestServer().CreateHandler() };
        var result = await probe.ProbeProfileAsync(f.Application.BaseUrl, new(f.Secret));
        Assert.IsNull(result.Diagnostics); Assert.AreEqual("not-installed", result.CustomerIdentity.Status);
        Assert.AreEqual("available", result.Operations!.Status); Assert.HasCount(6, result.Operations.Info!.Capabilities);
        Assert.AreEqual(f.Realm, result.Operations.Info.RealmId);
        Assert.HasCount(7, result.Capabilities);
        Assert.Contains(ManagementRegisteredOperations.Info, result.Capabilities);
    }

    [TestMethod]
    [DataRow("protocol")] [DataRow("realm")] [DataRow("scope")] [DataRow("fingerprint")]
    [DataRow("schema")] [DataRow("duplicate")] [DataRow("unregistered")] [DataRow("missing-prerequisite")]
    public async Task MalformedOrIncompatibleTargetMetadataNeverEnablesDispatch(string change)
    {
        await using var f = await Fixture.Create();
        var info = f.Connection.Operations!.Info!;
        var first = info.Capabilities[0];
        info = change switch
        {
            "protocol" => info with { ProtocolVersion = 99 },
            "realm" => info with { RealmId = Guid.Empty },
            "scope" => info with { RealmScope = "shared" },
            "fingerprint" => info with { Capabilities = [first with { DescriptorFingerprint = "unknown" }] },
            "schema" => info with { Capabilities = [first with { RequestSchemaVersion = 99 }] },
            "duplicate" => info with { Capabilities = [first, first] },
            "unregistered" => info with { Capabilities = [first with { Operation = "management.other.unknown" }] },
            _ => info with { Capabilities = info.Capabilities.Where(x => x.Operation == ManagementLicenseOperations.Suspend).ToArray() }
        };
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true, data = info }) };
        var result = await ManagementTargetConnectionProbe.ReadRegisteredInfoResponseAsync(response, f.Target.Services.GetRequiredService<ManagementOperationRegistry>(), default);
        Assert.AreEqual("incompatible", result.Status); Assert.IsNull(result.Info);
        Assert.AreEqual(0, f.SecretReads); Assert.AreEqual(0, f.WriteCount);
    }

    [TestMethod]
    [DataRow("lost")] [DataRow("malformed")] [DataRow("oversize")] [DataRow("redirect")]
    public async Task AmbiguousWriteRepliesNeverRetryAndCanBeReconciledReadonly(string failure)
    {
        await using var f = await Fixture.Create(); var version = (await f.Read()).Version;
        f.AfterSend = async (request, response) =>
        {
            if (request.Method != HttpMethod.Post) return response;
            response.Dispose();
            if (failure == "lost") throw new HttpRequestException("PRIVATE-TRANSPORT-ERROR");
            await Task.CompletedTask;
            return failure == "redirect" ? new(HttpStatusCode.TemporaryRedirect) { Headers = { Location = new("https://other.example") } } :
                new(HttpStatusCode.OK) { Content = new StringContent(failure == "oversize" ? new string('x', 400000) : "PRIVATE-MALFORMED") };
        };
        var uncertain = await Assert.ThrowsAsync<ManagementOperationUncertainException>(() => f.Suspend(version));
        Assert.AreEqual(1, f.WriteCount); f.AfterSend = null;
        var recovered = await f.Client.ReadReceiptAsync<ManagementLicenseTransitionResult>(f.AppId, ManagementLicenseOperations.Suspend, f.Tenant, f.License, uncertain.OperationId);
        Assert.AreEqual("suspended", recovered.Status); Assert.AreEqual(1, f.WriteCount);
        Assert.DoesNotContain("PRIVATE-", JsonSerializer.Serialize(f.Audits));
    }

    [TestMethod]
    public async Task ChangedSameIntentConflictsAndWildcardOrUnregisteredContractsNeverReachTarget()
    {
        await using var f = await Fixture.Create(); var version = (await f.Read()).Version;
        var first = await f.Suspend(version);
        await Assert.ThrowsAsync<ManagementOperationConflictException>(() => f.Suspend(first.Version!));
        var writes = f.WriteCount;
        await Assert.ThrowsAsync<ManagementOperationInvalidException>(() => f.Suspend("*"));
        await Assert.ThrowsAsync<ManagementAccessDeniedException>(() => f.Client.ExecuteAsync<ManagementLicenseSuspendRequest, ManagementLicenseTransitionResult>(f.AppId,
            "management.not.registered", f.Tenant, f.License, new(f.License, version), "synthetic-intent", version));
        Assert.AreEqual(writes, f.WriteCount);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Guid Org { get; } = Guid.NewGuid(); public Guid AppId { get; } = Guid.NewGuid(); public Guid Human { get; } = Guid.NewGuid();
        public Guid Realm { get; } = Guid.NewGuid(); public Guid Tenant { get; } = Guid.NewGuid(); public Guid License { get; private set; }
        public ManagementApplicationEntity Application { get; set; } = null!;
        public ManagementApplicationAccessEntity Assignment { get; set; } = null!;
        public ManagementApplicationCredentialEntity Connection { get; set; } = null!;
        public ManagementApiCredentialEntity Credential { get; set; } = null!;
        public bool UserEnabled { get; set; } = true;
        public int SecretReads; public int WriteCount;
        public string Secret { get; private set; } = "";
        public WebApplication Target { get; private set; } = null!;
        public ManagementRegisteredOperationClient Client { get; private set; } = null!;
        public IManagementRegisteredOperationAvailabilityService Availability { get; private set; } = null!;
        public ConcurrentBag<ManagementAuditEntity> Audits { get; } = [];
        public Func<HttpRequestMessage, Task>? BeforeSendAsync { get; set; }
        public Func<HttpRequestMessage, CancellationToken, Task>? TransportGate { get; set; }
        public Action<HttpRequestMessage>? BeforeSend { get; set; }
        public Func<HttpRequestMessage, HttpResponseMessage, Task<HttpResponseMessage>>? AfterSend { get; set; }
        public Captured? LastWrite { get; private set; }
        private readonly RSA rsa = RSA.Create(2048);
        private readonly List<IDisposable> owned = [];
        private static string[] Grants => [ManagementLicenseOperations.Read, ManagementLicenseOperations.Suspend, ManagementLicenseOperations.Restore,
            ManagementSeatOperations.Read, ManagementSeatOperations.Assign, ManagementSeatOperations.Unassign];
        public static async Task<Fixture> Create(bool includeSynthetic = false, int timeoutSeconds = 10, bool includeBulkContract = false)
        {
            var f = new Fixture(); var codec = new ManagementCredentialSecretCodec(); var credentialId = Guid.NewGuid(); f.Secret = codec.Create(credentialId);
            var grants = (includeSynthetic ? Grants.Append(SharedSenderSyntheticOperation) : Grants).Concat(includeBulkContract ? [ManagementBulkInvitationRevokeOperation.Name] : []).ToArray();
            f.Credential = new() { CredentialId = credentialId, ApplicationKey = "fixture", Environment = "Testing", SecretHash = codec.Hash(f.Secret),
                Capabilities = [ManagementRegisteredOperations.Info, .. grants], ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1), Version = "credential-1" };
            var key = new RsaSecurityKey(f.rsa) { KeyId = "fixture-signing" };
            var validation = new Mock<IManagementActorValidationKeyProvider>(); validation.Setup(x => x.GetTrustedKeys("fixture-portal")).Returns([key]);
            var credentials = new Mock<IManagementApiCredentialRepository>();
            credentials.Setup(x => x.GetAsync("fixture", "Testing", credentialId, It.IsAny<CancellationToken>())).ReturnsAsync(() => f.Credential);
            var replayIds = new ConcurrentDictionary<Guid, byte>(); var replay = new Mock<IManagementDelegationReplayStore>();
            replay.Setup(x => x.TryClaimAsync(f.Realm, "fixture-portal", It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, string _, Guid id, DateTimeOffset _, CancellationToken _) => replayIds.TryAdd(id, 0));
            var audit = new Mock<IManagementAuditRepository>(); audit.Setup(x => x.AppendAsync(It.IsAny<ManagementAuditEntity>(), It.IsAny<CancellationToken>()))
                .Callback<ManagementAuditEntity, CancellationToken>((entry, _) => f.Audits.Add(entry)).Returns(Task.CompletedTask);
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
            var config = new Dictionary<string, string?> { ["IBeam:Management:Diagnostics:ApplicationKey"] = "fixture", ["IBeam:Management:Diagnostics:Environment"] = "Testing",
                ["IBeam:Management:Operations:Target:RealmId"] = f.Realm.ToString("D"), ["IBeam:Management:Operations:Target:DedicatedApplicationRealm"] = "true",
                ["IBeam:Management:Operations:Target:TrustedPortals:0:OrganizationId"] = f.Org.ToString("D"), ["IBeam:Management:Operations:Target:TrustedPortals:0:ApplicationId"] = f.AppId.ToString("D"),
                ["IBeam:Management:Operations:Target:TrustedPortals:0:Issuer"] = "fixture-portal", ["IBeam:Management:Operations:Target:TrustedPortals:0:Audience"] = "fixture-target" };
            for (var i = 0; i < grants.Length; i++) config[$"IBeam:Management:Operations:Target:EnabledOperations:{i}"] = grants[i];
            builder.Configuration.AddInMemoryCollection(config);
            builder.Services.AddSingleton(credentials.Object); builder.Services.AddSingleton(validation.Object); builder.Services.AddSingleton(replay.Object); builder.Services.AddSingleton(audit.Object);
            builder.Services.AddIBeamLicensingServices(builder.Configuration);
            builder.Services.AddSingleton<ILicenseStatusRestorePolicy>(new Restore()); builder.Services.AddSingleton<ILicenseSeatSubjectPolicy>(new Subjects());
            builder.Services.AddIBeamManagementLicenseStatusTarget(o => { o.Enabled = true; o.EnableRestore = true; o.AllowVolatileDevelopmentStore = true; });
            builder.Services.AddIBeamManagementSeatsTarget(o => { o.Enabled = true; o.AllowVolatileDevelopmentStore = true; });
            if (includeSynthetic) builder.Services.AddIBeamManagementOperation<SharedSenderSyntheticRequest, SharedSenderSyntheticResult, SharedSenderSyntheticHandler>(SharedSenderSyntheticDescriptor);
            if (includeBulkContract) { builder.Services.AddSingleton(new BulkStubState()); builder.Services.AddIBeamManagementOperation<ManagementBulkInvitationRevokeRequest, ManagementBulkInvitationRevokeResult, BulkStubHandler>(ManagementBulkInvitationRevokeOperation.Descriptor); }
            builder.Services.AddIBeamManagementRegisteredOperationsTargetApi(builder.Configuration);
            f.Target = builder.Build(); f.Target.UseAuthentication(); f.Target.UseAuthorization(); f.Target.MapIBeamManagementRegisteredOperations();
            await f.Target.StartAsync();
            using (var scope = f.Target.Services.CreateScope())
            {
                var license = await scope.ServiceProvider.GetRequiredService<ITenantLicenseService>().GrantLicenseAsync(f.Tenant,
                    new() { PlanKey = "fixture", StartsUtc = DateTimeOffset.UtcNow.AddDays(-1), ExpiresUtc = DateTimeOffset.UtcNow.AddDays(1), SeatLimit = 3 });
                f.License = license.LicenseId;
            }
            var catalog = new ManagementOperationRegistry(ManagementLicensingServiceExtensions.LicenseStatusDescriptors.Concat(ManagementSeatServiceExtensions.SeatDescriptors)
                .Concat(includeSynthetic ? [SharedSenderSyntheticDescriptor] : []).Concat(includeBulkContract ? [ManagementBulkInvitationRevokeOperation.Descriptor] : []));
            var info = new ManagementRegisteredOperationInfo("fixture", "Testing", f.Realm, "dedicated-app", 1, catalog.Capabilities(f.Credential.Capabilities, grants, _ => true).ToArray());
            var connectionId = Guid.NewGuid();
            f.Application = new() { OrganizationId = f.Org, ApplicationId = f.AppId, ApplicationKey = "fixture", Environment = "Testing", BaseUrl = "https://fixture.example", ActiveCredentialId = connectionId, Version = "app-1" };
            f.Assignment = new() { OrganizationId = f.Org, ApplicationId = f.AppId, UserId = f.Human, Permissions = grants, Version = "assignment-1" };
            f.Connection = new() { OrganizationId = f.Org, ApplicationId = f.AppId, CredentialId = connectionId, BaseUrl = f.Application.BaseUrl, SecretReference = "fixture-reference",
                Capabilities = grants, Operations = new("available", info) };
            var principals = new Mock<IManagementPrincipalProvider>(); principals.Setup(x => x.GetPrincipal()).Returns(new ClaimsPrincipal(new ClaimsIdentity([
                new Claim("sub", f.Human.ToString("D")), new(ManagementClaimTypes.OrganizationId, f.Org.ToString("D")), new(ManagementClaimTypes.SubjectType, ManagementSubjectTypes.Human),
                new(ManagementClaimTypes.Permission, ManagementOperations.IdentityRead)], "trusted-management-fixture")));
            var apps = new Mock<IManagementApplicationStateProvider>(); apps.Setup(x => x.GetAsync(f.Org, f.AppId, It.IsAny<CancellationToken>())).ReturnsAsync(() => f.Application);
            var assignments = new Mock<IManagementApplicationAccessRepository>(); assignments.Setup(x => x.GetAsync(f.Org, f.AppId, f.Human, It.IsAny<CancellationToken>())).ReturnsAsync(() => f.Assignment);
            var users = new Mock<IManagementSupportUserStateProvider>(); users.Setup(x => x.GetAsync(f.Org, f.Human, It.IsAny<CancellationToken>())).ReturnsAsync(() => new(f.Org, f.Human, f.UserEnabled, false));
            var connections = new Mock<IManagementApplicationCredentialRepository>(); connections.Setup(x => x.GetAsync(f.Org, f.AppId, connectionId, It.IsAny<CancellationToken>())).ReturnsAsync(() => f.Connection);
            var secrets = new Mock<IManagementSecretStore>(); secrets.Setup(x => x.ReadAsync(new(f.Org, f.AppId), "fixture-reference", It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => { f.SecretReads++; return new(f.Secret); });
            var executor = new ServiceOperationExecutor();
            var access = new ManagementApplicationAccessService(assignments.Object, apps.Object, users.Object, principals.Object, executor, catalog);
            f.Availability = new ManagementRegisteredOperationAvailabilityService(catalog, principals.Object, apps.Object, connections.Object, access, executor);
            var signing = new Mock<IManagementActorSigningKeyProvider>(); signing.Setup(x => x.GetSigningCredentials()).Returns(new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
            var options = new StaticOptions<ManagementRegisteredOperationClientOptions>(new() { Issuer = "fixture-portal", OperationIdKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                Targets = [new() { ApplicationId = f.AppId, Audience = "fixture-target" }] });
            var connectionOptions = Options.Create(new ManagementConnectionOptions { ApprovedOrigins = [f.Application.BaseUrl], TimeoutSeconds = timeoutSeconds });
            var network = new ManagementConnectionNetworkPolicy(connectionOptions, Mock.Of<IManagementTargetDnsResolver>(), Mock.Of<IManagementTargetSocketConnector>());
            f.Client = new(catalog, principals.Object, access, f.Availability, apps.Object, connections.Object, secrets.Object, codec, signing.Object, new ManagementAuditContext(),
                network, connectionOptions, options, TimeProvider.System, executor) { TestTransportFactory = () => new Transport(f) { InnerHandler = f.Target.GetTestServer().CreateHandler() } };
            return f;
        }
        public Task<ManagementLicenseStatus> Read() => Client.ExecuteAsync<ManagementLicenseReadRequest, ManagementLicenseStatus>(AppId, ManagementLicenseOperations.Read, Tenant, License, new(), "", null);
        public Task<ManagementLicenseTransitionResult> Suspend(string version) => Client.ExecuteAsync<ManagementLicenseSuspendRequest, ManagementLicenseTransitionResult>(AppId,
            ManagementLicenseOperations.Suspend, Tenant, License, new(License, version), "suspend-intent", version);
        public string ChangeProof(string original, string field)
        {
            var handler = new JwtSecurityTokenHandler(); var parsed = handler.ReadJwtToken(original);
            if (field == "typ") parsed.Header["typ"] = "ibeam-management-actor+jwt";
            else if (field == "expired") { parsed.Payload["exp"] = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds(); }
            else parsed.Payload[field] = field is "iss" or "aud" ? "other-trust" : Guid.NewGuid().ToString("D");
            var header = new JwtHeader(new SigningCredentials(new RsaSecurityKey(rsa) { KeyId = "fixture-signing" }, SecurityAlgorithms.RsaSha256));
            header["typ"] = parsed.Header["typ"];
            return handler.WriteToken(new JwtSecurityToken(header, parsed.Payload));
        }
        public async Task<LicenseStatusSnapshot> NativeRead()
        { using var scope = Target.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<ITenantLicenseService>().ReadStatusAsync(Tenant, License); }
        public async ValueTask DisposeAsync() { foreach (var item in owned) item.Dispose(); await Target.DisposeAsync(); rsa.Dispose(); }
        private sealed class Transport(Fixture f) : DelegatingHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                if (request.Method == HttpMethod.Post)
                {
                    f.WriteCount++; f.LastWrite = new(request.RequestUri!, await request.Content!.ReadAsStringAsync(ct), request.Headers.GetValues("X-IBeam-Management-Actor").Single(),
                        request.Headers.GetValues("Idempotency-Key").Single(), request.Headers.GetValues("If-Match").Single());
                }
                f.BeforeSend?.Invoke(request); if (f.BeforeSendAsync is not null) await f.BeforeSendAsync(request);
                if (f.TransportGate is not null) await f.TransportGate(request, ct);
                var response = await base.SendAsync(request, ct);
                return f.AfterSend is null ? response : await f.AfterSend(request, response);
            }
        }
        private sealed class Restore : ILicenseStatusRestorePolicy
        { public Task RequireAsync(LicenseStatusCommand command, TenantLicenseRecord current, TenantLicenseRecord candidate, CancellationToken ct = default) => Task.CompletedTask; }
        private sealed class Subjects : ILicenseSeatSubjectPolicy
        { public Task RequireAsync(Guid tenantId, Guid userId, string transition, CancellationToken ct = default) => Task.CompletedTask; }
    }
    private sealed record Captured(Uri Uri, string Body, string Assertion, string Key, string Version)
    {
        public HttpRequestMessage Request(string secret)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, Uri) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
            request.Headers.Add("X-IBeam-Management-Key", secret); request.Headers.Add("X-IBeam-Management-Actor", Assertion);
            request.Headers.Add("Idempotency-Key", Key); request.Headers.TryAddWithoutValidation("If-Match", Version); return request;
        }
    }
    private sealed class StaticOptions<T>(T value) : IOptionsMonitor<T>
    { public T CurrentValue => value; public T Get(string? name) => value; public IDisposable? OnChange(Action<T, string?> listener) => null; }
}
