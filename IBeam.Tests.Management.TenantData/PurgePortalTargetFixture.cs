using System.Collections.Concurrent;
using System.Security.Cryptography;
using IBeam.Management;
using IBeam.Management.Entities;
using IBeam.Management.CustomerIdentity;
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

internal sealed class PurgePortalTargetFixture : IAsyncDisposable
{
    public DurablePurgeTests.Fixture Native { get; private set; } = null!;
    public Guid Tenant => Native.Execution.Authority.TenantId;
    public Guid Org => Native.Execution.Authority.OrganizationId;
    public Guid AppId => Native.Execution.Authority.ApplicationId;
    public Guid Realm => Native.Execution.Authority.RealmId;
    public Guid User => Native.Execution.Authority.ActorUserId;
    public ManagementApiCredentialEntity Credential = null!;
    public ConcurrentBag<ManagementAuditEntity> Audits { get; } = [];
    public WebApplication Server { get; private set; } = null!;
    private readonly RSA rsa = RSA.Create(2048);
    public string Secret { get; private set; } = "";
    public SigningCredentials Signing => new(new RsaSecurityKey(rsa) { KeyId = "purge-fixture-key" }, SecurityAlgorithms.RsaSha256);
    public static async Task<PurgePortalTargetFixture> Create()
    {
        var f = new PurgePortalTargetFixture { Native = await DurablePurgeTests.Fixture.Create() };
        var codec = new ManagementCredentialSecretCodec(); var id = Guid.NewGuid(); f.Secret = codec.Create(id);
        f.Credential = new() { CredentialId = id, ApplicationKey = "fixture", Environment = "Testing", Version = "credential-1", SecretHash = codec.Hash(f.Secret),
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1), Capabilities = [ManagementRegisteredOperations.Info, ManagementTenantDataOperations.Delete] };
        var keys = new Mock<IManagementActorValidationKeyProvider>(); keys.Setup(x => x.GetTrustedKeys("fixture-portal")).Returns([new RsaSecurityKey(f.rsa) { KeyId = "purge-fixture-key" }]);
        var credentials = new Mock<IManagementApiCredentialRepository>(); credentials.Setup(x => x.GetAsync("fixture", "Testing", id, It.IsAny<CancellationToken>())).ReturnsAsync(() => f.Credential);
        var audit = new Mock<IManagementAuditRepository>(); audit.Setup(x => x.AppendAsync(It.IsAny<ManagementAuditEntity>(), It.IsAny<CancellationToken>()))
            .Callback<ManagementAuditEntity, CancellationToken>((entry, _) => f.Audits.Add(entry)).Returns(Task.CompletedTask);
        var nonces = new ConcurrentDictionary<Guid, byte>(); var replay = new Mock<IManagementDelegationReplayStore>();
        replay.Setup(x => x.TryClaimAsync(f.Realm, "fixture-portal", It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string _, Guid nonce, DateTimeOffset _, CancellationToken _) => nonces.TryAdd(nonce, 0));
        var b = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" }); b.WebHost.UseTestServer(); b.Logging.ClearProviders();
        b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["IBeam:Management:Diagnostics:ApplicationKey"] = "fixture", ["IBeam:Management:Diagnostics:Environment"] = "Testing",
            ["IBeam:Management:Operations:Target:RealmId"] = f.Realm.ToString("D"), ["IBeam:Management:Operations:Target:DedicatedApplicationRealm"] = "true",
            ["IBeam:Management:Operations:Target:EnabledOperations:0"] = ManagementTenantDataOperations.Delete,
            ["IBeam:Management:Operations:Target:TrustedPortals:0:OrganizationId"] = f.Org.ToString("D"),
            ["IBeam:Management:Operations:Target:TrustedPortals:0:ApplicationId"] = f.AppId.ToString("D"),
            ["IBeam:Management:Operations:Target:TrustedPortals:0:Issuer"] = "fixture-portal",
            ["IBeam:Management:Operations:Target:TrustedPortals:0:Audience"] = "fixture-target" });
        b.Services.AddSingleton(keys.Object); b.Services.AddSingleton(credentials.Object); b.Services.AddSingleton(replay.Object); b.Services.AddSingleton(audit.Object);
        b.Services.AddSingleton<IManagementTenantPurgeJournalRepository>(f.Native.Store);
        b.Services.AddSingleton<IManagementTenantPurgeHandler>(f.Native.Native);
        b.Services.AddSingleton<IManagementTenantPurgePolicy>(f.Native.Native);
        b.Services.AddSingleton<IManagementTenantPurgeAccessFence>(f.Native.Native);
        b.Services.AddSingleton<TimeProvider>(f.Native.Clock);
        b.Services.AddIBeamManagementTenantPurgeJobs(b.Configuration);
        b.Services.AddIBeamManagementTenantPurgeTargetApi(b.Configuration, o => { o.Enabled = true; o.PolicyKeys = ["enterprise"]; });
        f.Server = b.Build(); f.Server.UseAuthentication(); f.Server.UseAuthorization(); f.Server.MapIBeamManagementRegisteredOperations();
        await f.Server.StartAsync(); return f;
    }
    public async Task Run(Guid job)
    {
        using var scope = Server.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IManagementTenantPurgeWorker>().RunAsync(Native.WithJob(job));
    }
    public async ValueTask DisposeAsync() { await Server.DisposeAsync(); await Native.DisposeAsync(); rsa.Dispose(); }
}
