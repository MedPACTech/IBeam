using System.Collections.Concurrent;
using IBeam.Identity.Interfaces;
using IBeam.Identity.Options;
using IBeam.Identity.Services.Tenants;
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
    public Guid Organization { get; private set; } = Guid.NewGuid();
    public Guid User { get; private set; } = Guid.NewGuid();
    public Guid Application { get; private set; } = Guid.NewGuid();
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
    public TenantAdministrationHooks TenantHooks { get; } = new();
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
        IManagementIdentityOperationRepository? operations = null, IManagementIdentityOperationAuditRepository? operationAudit = null,
        IIdentityTenantStore? tenantStore = null, IManagementTenantAdministrationRepository? tenantMetadata = null, bool compositeOnboarding = false,
        OnboardingFixture? onboarding = null, ProvisioningFixture? provisioning = null, InvitationLifecycleFixture? invitations = null,
        MembershipStorageFixture? memberships = null, IIdentityTenantMembershipConditionalStore? membershipWrites = null, bool tenantStatus = false, Guid? organization = null, Guid? supportUser = null, Guid? application = null,
        Guid? identityRealm = null, string applicationKey = "atlas", string environment = "Testing", Func<IBeam.Identity.Models.IdentityTenant, Task>? createdHook = null,
        IReadOnlyDictionary<string, string[]>? membershipPolicies = null)
    {
        var f = new DelegationFixture();
        f.Organization = organization ?? f.Organization; f.User = supportUser ?? f.User; f.Application = application ?? f.Application;
        f.Authorization.Setup(x => x.RequireAsync(f.Application, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() => f.Granted ? Task.CompletedTask : Task.FromException(new ManagementAccessDeniedException()));
        f.TenantHooks.CreatedAction = createdHook;
        var realmId = identityRealm ?? DiscoveryProtocolTests.Info.IdentityRealmId;
        f.Credentials.Row = f.Credentials.Row with { ApplicationKey = applicationKey, Environment = environment };
        if (tenantStatus) { tenantMetadata ??= new TenantMetadataStore(); operations ??= new OperationStore(); }
        if (memberships is not null) { operations ??= new OperationStore(); tenantStore ??= memberships.Tenants; }
        if (invitations is not null) {
            operations ??= new OperationStore(); tenantStore ??= invitations.Data.Data.Tenants.Store;
            var rows = new TenantRows(); rows.Rows.Clear(); rows.Rows.Add(new(invitations.Tenant.ToString("D"), "Private", "PRIVATE", "Active", null, null, "v1"));
            tenants ??= rows; invites ??= invitations;
        }
        if (onboarding is not null) { compositeOnboarding = true; tenantMetadata ??= new TenantMetadataStore(); operations ??= new OperationStore(); }
        if (provisioning is not null) { tenantMetadata ??= new TenantMetadataStore(); operations ??= new OperationStore(); tenantStore ??= provisioning.Data.Tenants.Store; }
        if (tenantMetadata is not null && operations is null) operations = new OperationStore();
        f.Credentials.Row = f.Credentials.Row with { Capabilities = f.Credentials.Row.Capabilities
            .Concat(users is null ? [] : new[] { ManagementCustomerIdentityOperations.UsersRead })
            .Concat(invites is null ? [] : new[] { ManagementCustomerIdentityOperations.InvitesRead })
            .Concat(operations is null ? [] : new[] { ManagementCustomerIdentityOperations.OnboardingRead }).ToArray() };
        if (tenantMetadata is not null) f.Credentials.Row = f.Credentials.Row with { Capabilities = f.Credentials.Row.Capabilities.Concat(new[] { ManagementCustomerIdentityOperations.TenantsUpdate }).ToArray() };
        var b = WebApplication.CreateBuilder(); b.Logging.ClearProviders(); b.WebHost.UseTestServer();
        if (compositeOnboarding) f.Credentials.Row = f.Credentials.Row with { Capabilities = f.Credentials.Row.Capabilities.Concat(
            ManagementCustomerIdentityPermissionPolicy.RequiredFor(onboarding is null ? ManagementCustomerIdentityOperations.OnboardingCreate : ManagementCustomerIdentityOperations.OnboardingRetry)).Distinct().ToArray() };
        if (provisioning is not null) f.Credentials.Row = f.Credentials.Row with { Capabilities = f.Credentials.Row.Capabilities.Concat(
            ManagementCustomerIdentityPermissionPolicy.RequiredFor(ManagementCustomerIdentityOperations.UsersProvision)).Append(ManagementCustomerIdentityOperations.RolesRead).Distinct().ToArray() };
        if (invitations is not null) f.Credentials.Row = f.Credentials.Row with { Capabilities = f.Credentials.Row.Capabilities.Append(ManagementCustomerIdentityOperations.InvitesManage).ToArray() };
        if (memberships is not null) f.Credentials.Row = f.Credentials.Row with { Capabilities = f.Credentials.Row.Capabilities.Concat(new[] {
            ManagementCustomerIdentityOperations.MembershipsManage, ManagementCustomerIdentityOperations.RolesAssign }).Distinct().ToArray() };
        if (tenantStatus) f.Credentials.Row = f.Credentials.Row with { Capabilities = f.Credentials.Row.Capabilities.Append(ManagementCustomerIdentityOperations.TenantsStatus).ToArray() };
        b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["IBeam:Management:Diagnostics:ApplicationKey"] = applicationKey, ["IBeam:Management:Diagnostics:Environment"] = environment,
            ["IBeam:Management:CustomerIdentity:IdentityRealmId"] = realmId.ToString(),
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
        if (tenantMetadata is not null) b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["IBeam:Management:CustomerIdentity:EnabledOperations:5"] = ManagementCustomerIdentityOperations.TenantsUpdate });
        if (compositeOnboarding || provisioning is not null || invitations is not null || memberships is not null || tenantStatus) b.Configuration.AddInMemoryCollection(ManagementCustomerIdentityOperations.All.Where(ManagementCustomerIdentityPermissionPolicy.IsAssignable)
            .Select((operation, index) => new KeyValuePair<string, string?>("IBeam:Management:CustomerIdentity:EnabledOperations:" + index, operation)));
        b.Services.AddSingleton(f);
        b.Services.AddSingleton<IManagementApiCredentialRepository>(f.Credentials);
        b.Services.AddSingleton<IManagementActorValidationKeyProvider>(f);
        b.Services.AddSingleton<IManagementDelegationReplayStore>(f.Replay);
        b.Services.AddSingleton<IManagementAuditRepository>(f.Audit);
        b.Services.AddSingleton<IProbeAdapter, ProbeAdapter>();
        b.Services.AddSingleton<IManagementCustomerIdentityCapability>(new Capability(ManagementCustomerIdentityOperations.TenantsRead));
        b.Services.AddSingleton<IManagementCustomerIdentityCapability>(new Capability(ManagementCustomerIdentityOperations.TenantsCreate));
        if (compositeOnboarding) foreach (var operation in ManagementCustomerIdentityPermissionPolicy.RequiredFor(ManagementCustomerIdentityOperations.OnboardingCreate))
            b.Services.AddSingleton<IManagementCustomerIdentityCapability>(new Capability(operation));
        b.Services.AddControllers().ConfigureApplicationPartManager(x => x.ApplicationParts.Clear());
        b.Services.AddIBeamManagementCustomerIdentityTargetApi(b.Configuration);
        b.Services.AddIBeamManagementDelegatedActors(b.Configuration);
        if (operations is not null) {
            b.Services.AddSingleton(operations);
            b.Services.AddSingleton<IManagementIdentityOperationAuditRepository>(operationAudit ?? new OperationFixture());
            b.Services.AddIBeamManagementIdentityOperationsApi(b.Configuration);
        }
        if (tenantMetadata is not null) {
            b.Services.AddSingleton<IIdentityTenantStore>(tenantStore ?? new ConditionalTenantStore());
            b.Services.AddScoped<IIdentityTenantService, IdentityTenantService>();
            b.Services.AddSingleton<ITenantExtensionCoordinator>(f.TenantHooks);
            b.Services.AddSingleton(Mock.Of<IAuthEventPublisher>());
            b.Services.AddSingleton(Mock.Of<IAuthLifecycleHook>());
            b.Services.Configure<AuthEventOptions>(x => x.StrictPublishFailures = true);
            b.Services.AddSingleton(tenantMetadata);
            b.Services.AddIBeamManagementTenantAdministrationApi(b.Configuration);
        }
        if (onboarding is not null) {
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["IBeam:Management:CustomerIdentity:Onboarding:Policies:enterprise-owner"] = "Owner" });
            b.Services.AddSingleton(onboarding.External); b.Services.AddSingleton(onboarding.InviteStore); b.Services.AddSingleton(onboarding.RoleStore);
            b.Services.AddSingleton(onboarding.Users.Object); b.Services.AddSingleton(onboarding.Memberships.Object);
            b.Services.AddSingleton<IManagementEnterpriseOnboardingRepository>(onboarding.Jobs);
            b.Services.AddIBeamManagementEnterpriseOnboardingApi(b.Configuration);
        }
        if (provisioning is not null) {
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["IBeam:Management:CustomerIdentity:Provisioning:Policies:default:0"] = "Member" });
            b.Services.AddSingleton(provisioning.Data.External); b.Services.AddSingleton(provisioning.Data.InviteStore);
            b.Services.AddSingleton(provisioning.Data.Users.Object); b.Services.AddSingleton(provisioning.Data.Memberships.Object);
            b.Services.AddSingleton<IManagementCustomerProvisioningRepository>(provisioning.Journal);
            b.Services.AddSingleton<IManagementAssignableRoleReadRepository>(provisioning);
            b.Services.AddSingleton<IManagementCustomerInvitationReadRepository>(provisioning);
            b.Services.AddIBeamManagementCustomerProvisioningApi(b.Configuration);
        }
        if (invitations is not null) {
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["IBeam:Management:CustomerIdentity:Invitations:ApprovedRoleNames:0"] = "Member" });
            b.Services.AddSingleton<IIdentityTenantStore>(tenantStore!);
            b.Services.AddSingleton<ITenantInviteStore>(invitations.Native.Object);
            b.Services.AddSingleton<IManagedTenantInviteStore>(invitations);
            b.Services.AddSingleton<IManagedTenantInviteDeliveryService>(invitations);
            b.Services.AddSingleton<IManagementAssignableRoleReadRepository>(invitations.Data);
            b.Services.AddSingleton<IManagementCustomerInvitationTransitionRepository>(invitations.Journal);
            b.Services.AddSingleton<IManagementCustomerInvitationReadRepository>(invitations);
            b.Services.AddIBeamManagementCustomerInvitationsApi(b.Configuration);
        }
        if (memberships is not null) {
            if (membershipPolicies is not null) b.Configuration.AddInMemoryCollection(membershipPolicies.SelectMany(policy => policy.Value.Select((role, index) =>
                new KeyValuePair<string, string?>($"IBeam:Management:CustomerIdentity:Provisioning:Policies:{policy.Key}:{index}", role))));
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
                ["IBeam:Management:CustomerIdentity:Provisioning:Policies:default:0"] = "Member",
                ["IBeam:Management:CustomerIdentity:Provisioning:Policies:administrator:0"] = "Owner",
                ["IBeam:Management:CustomerIdentity:Memberships:AdministratorRoleNames:0"] = "Owner" });
            b.Services.PostConfigure<IBeam.Management.Identity.Target.Services.Memberships.ManagementCustomerMembershipOptions>(x => x.AdministratorRoleNames = ["Owner"]);
            b.Services.AddSingleton<IIdentityTenantStore>(memberships.Tenants); b.Services.AddSingleton<ITenantMembershipStore>(memberships.Members);
            b.Services.AddSingleton<IIdentityTenantMembershipConditionalStore>(membershipWrites ?? memberships.Members);
            b.Services.AddSingleton<ITenantRoleStore>(memberships.Roles);
            b.Services.AddSingleton<IManagementAssignableRoleReadRepository>(new IBeam.Management.Repositories.AzureTable.AzureTableManagementAssignableRoleReadRepository(memberships.RoleTable));
            b.Services.AddSingleton<IIdentityTenantAccessEnforcement, TestMembershipEnforcement>();
            b.Services.AddIBeamManagementCustomerMembershipApi(b.Configuration);
        }
        if (tenantStatus) { b.Services.AddSingleton<IIdentityTenantAccessEnforcement, TestMembershipEnforcement>(); b.Services.AddIBeamManagementTenantStatusApi(b.Configuration); }
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
        var app = new ManagementApplicationEntity { OrganizationId = f.Organization, ApplicationId = f.Application, ApplicationKey = applicationKey,
            Environment = environment, BaseUrl = "https://atlas.example", IsEnabled = true, ActiveCredentialId = Guid.NewGuid(), Version = "app-v1" };
        var applications = new Mock<IManagementApplicationStateProvider>();
        applications.Setup(x => x.GetAsync(f.Organization, f.Application, It.IsAny<CancellationToken>())).ReturnsAsync(app);
        var connection = new ManagementApplicationCredentialEntity { OrganizationId = f.Organization, ApplicationId = f.Application,
            CredentialId = app.ActiveCredentialId.Value, BaseUrl = app.BaseUrl, SecretReference = "private-fixture-reference",
            Capabilities = f.Credentials.Row.Capabilities, CustomerIdentity = new("available", DiscoveryProtocolTests.Info with { ApplicationKey = applicationKey, Environment = environment, IdentityRealmId = realmId, Capabilities = f.Credentials.Row.Capabilities }) };
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
    public Task<ManagementActorAssertion> Issue(string operation, string method = "GET", string path = "/api/management-identity/probe", byte[]? body = null,
        string idempotencyKey = "fixture-idempotency", string? ifMatch = null)
        => Issuer.IssueAsync(Application, operation, new() { Method = method, PathAndQuery = path, Body = body ?? [], IdempotencyKey = method == "GET" ? null : idempotencyKey, IfMatch = ifMatch });
    public async Task<HttpResponseMessage> Send(ManagementActorAssertion? assertion, string method = "GET", string path = "/api/management-identity/probe", byte[]? body = null,
        string idempotencyKey = "fixture-idempotency", string? ifMatch = null)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("X-IBeam-Management-Key", Key);
        if (assertion is not null) request.Headers.Add("X-IBeam-Management-Actor", assertion.Value);
        if (method != "GET") request.Headers.Add("Idempotency-Key", idempotencyKey);
        if (ifMatch is not null) request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
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
    private sealed class TestMembershipEnforcement : IIdentityTenantAccessEnforcement
    { public int ProtocolVersion => 1; public string Mode => "fixture-custom-auth-integration"; }
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
    [HttpPost("onboarding"), ManagementCustomerIdentityOperation(ManagementCustomerIdentityOperations.OnboardingCreate)]
    public async Task<IActionResult> Composite(CancellationToken ct)
    {
        var parent = await access.RequireAsync(ManagementCustomerIdentityOperations.OnboardingCreate, ct);
        var child = await access.RequireAsync(ManagementCustomerIdentityOperations.TenantsCreate, ct);
        var context = HttpContext.RequestServices.GetRequiredService<IManagementDelegatedActorContext>().Current!;
        var fixture = HttpContext.RequestServices.GetRequiredService<DelegationFixture>();
        fixture.Credentials.Row = fixture.Credentials.Row with { Capabilities = fixture.Credentials.Row.Capabilities.Where(x => x != ManagementCustomerIdentityOperations.RolesAssign).ToArray() };
        var denied = false;
        try { await access.RequireAsync(ManagementCustomerIdentityOperations.TenantsCreate, ct); } catch (ManagementAccessDeniedException) { denied = true; }
        return OkResponse(new { parent = parent.Operation, child = child.Operation, original = context.Operation, denied, child.UserId });
    }
}
