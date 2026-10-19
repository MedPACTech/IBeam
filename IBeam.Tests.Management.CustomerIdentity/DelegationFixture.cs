using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Reflection;
using IBeam.Api.Controllers;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Entities;
using IBeam.Management.Identity.Target.Api;
using IBeam.Management.Identity.Target.Services.Delegation;
using IBeam.Management.Services;
using IBeam.Management.Services.Audit;
using IBeam.Management.Services.Credentials;
using IBeam.Management.Services.CustomerIdentity.Delegation;
using IBeam.Services.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
namespace IBeam.Tests.Management.CustomerIdentity;

internal sealed class DelegationFixture : IAsyncDisposable, IManagementActorSigningKeyProvider, IManagementActorValidationKeyProvider
{
    public Guid Organization { get; } = Guid.NewGuid();
    public Guid User { get; } = Guid.NewGuid();
    public Guid Application { get; } = Guid.NewGuid();
    public TargetCredentials Credentials { get; } = new();
    public string Key { get; }
    public RSA Rsa { get; } = RSA.Create(2048);
    public List<SecurityKey> VerificationKeys { get; } = [];
    public SigningCredentials Signing => new(new RsaSecurityKey(Rsa) { KeyId = "active-key" }, SecurityAlgorithms.RsaSha256);
    public Mock<IManagementCustomerIdentityAuthorizationService> Authorization { get; } = new();
    public bool Granted { get; set; } = true;
    public int Executions { get; set; }
    public ReplayStore Replay { get; } = new();
    public AuditStore Audit { get; } = new();
    public WebApplication App { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public IManagementDelegatedActorProvider Issuer { get; private set; } = null!;
    public ClaimsPrincipal Human => new(new ClaimsIdentity([
        new("sub", User.ToString("D")), new(ManagementClaimTypes.OrganizationId, Organization.ToString("D")),
        new(ManagementClaimTypes.SubjectType, ManagementSubjectTypes.Human), new(ManagementClaimTypes.Permission, ManagementOperations.IdentityRead) ], "Support"));
    public SigningCredentials GetSigningCredentials() => Signing;
    public IReadOnlyList<SecurityKey> GetTrustedKeys(string configuredIssuer)
    {
        Assert.AreEqual("test-support-issuer", configuredIssuer);
        return VerificationKeys;
    }
    private DelegationFixture()
    {
        var codec = new ManagementCredentialSecretCodec(); Key = codec.Create(Credentials.Row.CredentialId);
        Credentials.Row = Credentials.Row with { SecretHash = codec.Hash(Key), Capabilities = [
            ManagementCustomerIdentityOperations.Info, ManagementCustomerIdentityOperations.TenantsRead, ManagementCustomerIdentityOperations.TenantsCreate] };
        VerificationKeys.Add(new RsaSecurityKey(Rsa.ExportParameters(false)) { KeyId = "active-key" });
        Authorization.Setup(x => x.RequireAsync(Application, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() => Granted ? Task.CompletedTask : Task.FromException(new ManagementAccessDeniedException()));
    }
    public static async Task<DelegationFixture> Start(IManagementCustomerTenantReadRepository? tenants = null,
        IManagementCustomerUserReadRepository? users = null, IManagementCustomerInvitationReadRepository? invites = null,
        IManagementIdentityOperationRepository? operations = null, IManagementIdentityOperationAuditRepository? operationAudit = null)
    {
        var f = new DelegationFixture();
        f.Credentials.Row = f.Credentials.Row with { Capabilities = f.Credentials.Row.Capabilities
            .Concat(users is null ? [] : new[] { ManagementCustomerIdentityOperations.UsersRead })
            .Concat(invites is null ? [] : new[] { ManagementCustomerIdentityOperations.InvitesRead })
            .Concat(operations is null ? [] : new[] { ManagementCustomerIdentityOperations.OnboardingRead }).ToArray() };
        var b = WebApplication.CreateBuilder(); b.Logging.ClearProviders(); b.WebHost.UseTestServer();
        b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["IBeam:Management:Diagnostics:ApplicationKey"] = "atlas", ["IBeam:Management:Diagnostics:Environment"] = "Testing",
            ["IBeam:Management:CustomerIdentity:IdentityRealmId"] = DiscoveryProtocolTests.Info.IdentityRealmId.ToString(),
            ["IBeam:Management:CustomerIdentity:DedicatedApplicationRealm"] = "true",
            ["IBeam:Management:CustomerIdentity:EnabledOperations:0"] = ManagementCustomerIdentityOperations.TenantsRead,
            ["IBeam:Management:CustomerIdentity:EnabledOperations:1"] = ManagementCustomerIdentityOperations.TenantsCreate,
            ["IBeam:Management:CustomerIdentity:Delegation:TrustedPortals:0:OrganizationId"] = f.Organization.ToString(),
            ["IBeam:Management:CustomerIdentity:Delegation:TrustedPortals:0:ApplicationId"] = f.Application.ToString(),
            ["IBeam:Management:CustomerIdentity:Delegation:TrustedPortals:0:Issuer"] = "test-support-issuer",
            ["IBeam:Management:CustomerIdentity:Delegation:TrustedPortals:0:Audience"] = "test-atlas-management",
            ["IBeam:Management:CustomerIdentity:TenantDirectory:ProtectionKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["IBeam:Management:CustomerIdentity:UserDirectory:ProtectionKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["IBeam:Management:CustomerIdentity:EnabledOperations:2"] = ManagementCustomerIdentityOperations.UsersRead,
            ["IBeam:Management:CustomerIdentity:EnabledOperations:3"] = ManagementCustomerIdentityOperations.InvitesRead });
        if (operations is not null) b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["IBeam:Management:CustomerIdentity:EnabledOperations:4"] = ManagementCustomerIdentityOperations.OnboardingRead,
            ["IBeam:Management:CustomerIdentity:Operations:FingerprintKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) });
        b.Services.AddSingleton(f);
        b.Services.AddSingleton<IManagementApiCredentialRepository>(f.Credentials);
        b.Services.AddSingleton<IManagementActorValidationKeyProvider>(f);
        b.Services.AddSingleton<IManagementDelegationReplayStore>(f.Replay);
        b.Services.AddSingleton<IManagementAuditRepository>(f.Audit);
        b.Services.AddSingleton<IProbeAdapter, ProbeAdapter>();
        b.Services.AddSingleton<IManagementCustomerIdentityCapability>(new Capability(ManagementCustomerIdentityOperations.TenantsRead));
        b.Services.AddSingleton<IManagementCustomerIdentityCapability>(new Capability(ManagementCustomerIdentityOperations.TenantsCreate));
        b.Services.AddControllers().ConfigureApplicationPartManager(x => x.ApplicationParts.Clear());
        b.Services.AddIBeamManagementCustomerIdentityTargetApi(b.Configuration);
        b.Services.AddIBeamManagementDelegatedActors(b.Configuration);
        if (operations is not null) {
            b.Services.AddSingleton(operations);
            b.Services.AddSingleton<IManagementIdentityOperationAuditRepository>(operationAudit ?? new OperationFixture());
            b.Services.AddIBeamManagementIdentityOperationsApi(b.Configuration);
        }
        if (tenants is not null)
        {
            b.Services.AddSingleton<IManagementCustomerTenantReadRepository>(tenants);
            b.Services.AddIBeamManagementTenantDirectoryApi(b.Configuration);
        }
        if (users is not null)
        {
            b.Services.AddSingleton<IManagementCustomerUserReadRepository>(users);
            b.Services.AddIBeamManagementCustomerUserDirectoryApi(b.Configuration);
        }
        if (invites is not null)
        {
            b.Services.AddSingleton<IManagementCustomerInvitationReadRepository>(invites);
            b.Services.AddIBeamManagementCustomerInvitationDirectoryApi(b.Configuration);
        }
        b.Services.AddControllers().ConfigureApplicationPartManager(x => x.FeatureProviders.Add(new ProbeFeature()));
        f.App = b.Build(); f.App.UseAuthentication(); f.App.UseAuthorization(); f.App.MapControllers();
        try { await f.App.StartAsync(); f.Client = f.App.GetTestClient(); }
        catch { await f.App.DisposeAsync(); f.Rsa.Dispose(); throw; }
        var app = new ManagementApplicationEntity { OrganizationId = f.Organization, ApplicationId = f.Application, ApplicationKey = "atlas",
            Environment = "Testing", BaseUrl = "https://atlas.example", IsEnabled = true, ActiveCredentialId = Guid.NewGuid(), Version = "app-v1" };
        var applications = new Mock<IManagementApplicationStateProvider>();
        applications.Setup(x => x.GetAsync(f.Organization, f.Application, It.IsAny<CancellationToken>())).ReturnsAsync(app);
        var connection = new ManagementApplicationCredentialEntity { OrganizationId = f.Organization, ApplicationId = f.Application,
            CredentialId = app.ActiveCredentialId.Value, BaseUrl = app.BaseUrl, SecretReference = "private-fixture-reference",
            Capabilities = f.Credentials.Row.Capabilities, CustomerIdentity = new("available", DiscoveryProtocolTests.Info with { Capabilities = f.Credentials.Row.Capabilities }) };
        var credentials = new Mock<IManagementApplicationCredentialRepository>();
        credentials.Setup(x => x.GetAsync(f.Organization, f.Application, connection.CredentialId, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        var secrets = new Mock<IManagementSecretStore>();
        secrets.Setup(x => x.ReadAsync(new(f.Organization, f.Application), connection.SecretReference, It.IsAny<CancellationToken>())).ReturnsAsync(new ManagementSecretValue(f.Key));
        var principal = Mock.Of<IManagementPrincipalProvider>(x => x.GetPrincipal() == f.Human);
        f.Issuer = new ManagementDelegatedActorProvider(f.Authorization.Object, principal, applications.Object, credentials.Object, secrets.Object,
            new ManagementCredentialSecretCodec(), f, new Monitor<ManagementActorIssuerOptions>(new() {
                Issuer = "test-support-issuer", LifetimeSeconds = 30, Targets = [new() { ApplicationId = f.Application, Audience = "test-atlas-management" }] }),
            new ManagementAuditContext(), TimeProvider.System, new ServiceOperationExecutor());
        return f;
    }
    public Task<ManagementActorAssertion> Issue(string operation, string method = "GET", string path = "/api/management-identity/probe", byte[]? body = null)
        => Issuer.IssueAsync(Application, operation, new() { Method = method, PathAndQuery = path, Body = body ?? [], IdempotencyKey = method == "GET" ? null : "fixture-idempotency" });
    public async Task<HttpResponseMessage> Send(ManagementActorAssertion? assertion, string method = "GET", string path = "/api/management-identity/probe", byte[]? body = null)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("X-IBeam-Management-Key", Key);
        if (assertion is not null) request.Headers.Add("X-IBeam-Management-Actor", assertion.Value);
        if (method != "GET") request.Headers.Add("Idempotency-Key", "fixture-idempotency");
        if (body is not null) { request.Content = new ByteArrayContent(body); request.Content.Headers.ContentType = new("application/json"); }
        return await Client.SendAsync(request);
    }
    public ManagementActorAssertion Change(ManagementActorAssertion assertion, Action<JwtPayload> mutate, SigningCredentials? signing = null, string? type = null)
    {
        var source = new JwtSecurityTokenHandler().ReadJwtToken(assertion.Value);
        mutate(source.Payload);
        var header = new JwtHeader(signing ?? Signing); header["typ"] = type ?? "ibeam-management-actor+jwt";
        return new() { Value = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(header, source.Payload)), ExpiresUtc = assertion.ExpiresUtc };
    }
    public async ValueTask DisposeAsync() { Client.Dispose(); await App.DisposeAsync(); Rsa.Dispose(); }
    private sealed class Capability(string operation) : IManagementCustomerIdentityCapability
    {
        public string Operation => operation;
        public Type RequiredServiceType => typeof(IProbeAdapter);
    }
    private sealed class ProbeFeature : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) => feature.Controllers.Add(typeof(DelegationProbeController).GetTypeInfo());
    }
    private interface IProbeAdapter;
    private sealed class ProbeAdapter : IProbeAdapter;
}
internal sealed class ReplayStore : IManagementDelegationReplayStore
{
    public ConcurrentDictionary<(Guid, string, Guid), DateTimeOffset> Claims { get; } = [];
    public Task<bool> TryClaimAsync(Guid realm, string issuer, Guid id, DateTimeOffset expires, CancellationToken ct = default)
        => Task.FromResult(Claims.TryAdd((realm, issuer, id), expires));
}
internal sealed class AuditStore : IManagementAuditRepository
{
    public ConcurrentBag<ManagementAuditEntity> Rows { get; } = [];
    public Task AppendAsync(ManagementAuditEntity entry, CancellationToken ct = default) { Rows.Add(entry); return Task.CompletedTask; }
}
internal sealed class Monitor<T>(T value) : IOptionsMonitor<T> where T : class
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
[NonController, Authorize(Policy = ManagementCustomerIdentityTargetApiExtensions.Policy), HttpLogging(HttpLoggingFields.None)]
[Route("api/management-identity/probe")]
public sealed class DelegationProbeController(IManagementCustomerIdentityTargetAccessService access) : ApiControllerBase
{
    [HttpGet, ManagementCustomerIdentityOperation(ManagementCustomerIdentityOperations.TenantsRead)]
    public async Task<IActionResult> Read(CancellationToken ct)
    {
        var actor = await access.RequireAsync(ManagementCustomerIdentityOperations.TenantsRead, ct);
        return OkResponse(new { actor.UserId, actor.OrganizationId, actor.CredentialId,
            subjectType = User.FindFirst(ManagementClaimTypes.SubjectType)?.Value });
    }
    [HttpPost, ManagementCustomerIdentityOperation(ManagementCustomerIdentityOperations.TenantsCreate)]
    public async Task<IActionResult> Write(CancellationToken ct)
    {
        var actor = await access.RequireAsync(ManagementCustomerIdentityOperations.TenantsCreate, ct);
        HttpContext.RequestServices.GetRequiredService<DelegationFixture>().Executions++;
        return OkResponse(new { actor.UserId, actor.CredentialId });
    }
}
