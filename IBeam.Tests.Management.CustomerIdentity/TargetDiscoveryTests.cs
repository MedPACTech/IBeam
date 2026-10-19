using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using IBeam.Management;
using IBeam.Management.CustomerIdentity;
using IBeam.Management.Entities;
using IBeam.Management.Identity.Target.Api;
using IBeam.Management.Services.Credentials;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
namespace IBeam.Tests.Management.CustomerIdentity;

[TestClass]
public sealed class TargetDiscoveryTests
{
    [TestMethod]
    public async Task IdentityOnlyTargetUsesRealMachineValidationAndRejectsRevocation()
    {
        await using var fixture = await TargetFixture.Start();
        using var response = await fixture.Get();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("no-store", response.Headers.CacheControl!.ToString());
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(fixture.Key, text);
        using var json = JsonDocument.Parse(text);
        var data = json.RootElement.GetProperty("data");
        Assert.AreEqual(DiscoveryProtocolTests.Info.IdentityRealmId, data.GetProperty("identityRealmId").GetGuid());
        CollectionAssert.AreEqual(new[] { ManagementCustomerIdentityOperations.Info },
            data.GetProperty("capabilities").EnumerateArray().Select(x => x.GetString()!).ToArray());
        using var noDiagnostics = await fixture.Client.GetAsync("/api/management-diagnostics/info");
        Assert.AreEqual(HttpStatusCode.NotFound, noDiagnostics.StatusCode);
        using var noDirectory = await fixture.Client.GetAsync("/api/management-identity/tenants");
        Assert.AreEqual(HttpStatusCode.NotFound, noDirectory.StatusCode);
        fixture.Credentials.Row = fixture.Credentials.Row with { RevokedUtc = DateTimeOffset.UtcNow };
        using var revoked = await fixture.Get();
        Assert.AreEqual(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }
    [TestMethod]
    [DataRow("missing")] [DataRow("customer")] [DataRow("mixed")] [DataRow("unknown-key")] [DataRow("no-info-capability")] [DataRow("expired")]
    public async Task CustomerAndInvalidMachineCredentialsCannotReadDiscovery(string mode)
    {
        await using var fixture = await TargetFixture.Start();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/management-identity/info");
        if (mode is not ("missing" or "customer"))
            request.Headers.Add("X-IBeam-Management-Key", mode == "unknown-key" ? "invalid" : fixture.Key);
        if (mode is "customer" or "mixed") request.Headers.Add("Authorization", "Bearer customer");
        if (mode == "no-info-capability") fixture.Credentials.Row = fixture.Credentials.Row with { Capabilities = [ManagementOperations.DiagnosticsInfo] };
        if (mode == "expired") fixture.Credentials.Row = fixture.Credentials.Row with { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1) };
        using var response = await fixture.Client.SendAsync(request);
        Assert.AreEqual(mode == "no-info-capability" ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized, response.StatusCode);
    }
    [TestMethod]
    public async Task MereAssemblyReferenceDoesNotExposeRoutesAndCustomerDefaultIsPreserved()
    {
        await using var fixture = await TargetFixture.Start(install: false);
        using var noRoute = await fixture.Get();
        Assert.AreEqual(HttpStatusCode.NotFound, noRoute.StatusCode);
        await using var installed = await TargetFixture.Start();
        installed.Client.DefaultRequestHeaders.Authorization = new("Bearer", "customer");
        using var customer = await installed.Client.GetAsync("/customer");
        Assert.AreEqual(HttpStatusCode.OK, customer.StatusCode);
        installed.Client.DefaultRequestHeaders.Authorization = null;
        using var request = new HttpRequestMessage(HttpMethod.Get, "/customer");
        request.Headers.Add("X-IBeam-Management-Key", installed.Key);
        using var machineOnCustomer = await installed.Client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Unauthorized, machineOnCustomer.StatusCode);
    }
    [TestMethod]
    [DataRow("missing-realm")] [DataRow("shared-realm")] [DataRow("missing-binding")]
    public async Task AmbiguousRealmOrMissingDeploymentFailsAtStartup(string mode)
    {
        await Assert.ThrowsAsync<OptionsValidationException>(async () => { await using var fixture = await TargetFixture.Start(invalid: mode); });
    }
    [TestMethod]
    public async Task MissingAdapterDoesNotAdvertiseFutureDirectoryOperations()
    {
        await using var fixture = await TargetFixture.Start(registerMissingAdapter: true);
        using var response = await fixture.Get();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(ManagementCustomerIdentityOperations.TenantsRead, await response.Content.ReadAsStringAsync());
    }
}
internal sealed class TargetFixture : IAsyncDisposable
{
    public TargetCredentials Credentials { get; } = new();
    public string Key { get; }
    public WebApplication App { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    private TargetFixture()
    {
        var codec = new ManagementCredentialSecretCodec();
        Key = codec.Create(Credentials.Row.CredentialId);
        Credentials.Row = Credentials.Row with { SecretHash = codec.Hash(Key) };
    }
    public static async Task<TargetFixture> Start(bool install = true, string? invalid = null, bool registerMissingAdapter = false)
    {
        var f = new TargetFixture();
        var b = WebApplication.CreateBuilder();
        b.Logging.ClearProviders(); b.WebHost.UseTestServer();
        b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["IBeam:Management:Diagnostics:ApplicationKey"] = invalid == "missing-binding" ? "" : "atlas",
            ["IBeam:Management:Diagnostics:Environment"] = "Testing",
            ["IBeam:Management:CustomerIdentity:IdentityRealmId"] = invalid == "missing-realm" ? Guid.Empty.ToString() : DiscoveryProtocolTests.Info.IdentityRealmId.ToString(),
            ["IBeam:Management:CustomerIdentity:DedicatedApplicationRealm"] = invalid == "shared-realm" ? "false" : "true" });
        b.Services.AddAuthentication("Customer").AddScheme<AuthenticationSchemeOptions, FixtureCustomerAuthentication>("Customer", _ => {});
        b.Services.AddAuthorization();
        b.Services.AddSingleton<IManagementApiCredentialRepository>(f.Credentials);
        b.Services.AddControllers().ConfigureApplicationPartManager(x => x.ApplicationParts.Clear())
            .AddApplicationPart(typeof(ManagementCustomerIdentityInfoController).Assembly);
        if (install) b.Services.AddIBeamManagementCustomerIdentityTargetApi(b.Configuration);
        if (registerMissingAdapter) b.Services.AddSingleton<IManagementCustomerIdentityCapability>(new MissingAdapter());
        f.App = b.Build(); f.App.UseAuthentication(); f.App.UseAuthorization(); f.App.MapControllers();
        f.App.MapGet("/customer", () => "customer").RequireAuthorization();
        try { await f.App.StartAsync(); f.Client = f.App.GetTestClient(); return f; }
        catch { await f.App.DisposeAsync(); throw; }
    }
    public Task<HttpResponseMessage> Get()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/management-identity/info");
        request.Headers.Add("X-IBeam-Management-Key", Key);
        return Client.SendAsync(request);
    }
    public async ValueTask DisposeAsync() { Client.Dispose(); await App.DisposeAsync(); }
    private sealed class MissingAdapter : IManagementCustomerIdentityCapability
    {
        public string Operation => ManagementCustomerIdentityOperations.TenantsRead;
        public Type RequiredServiceType => typeof(IManagementTenantDirectoryQuery);
    }
}
internal sealed class TargetCredentials : IManagementApiCredentialRepository
{
    public ManagementApiCredentialEntity Row { get; set; } = new() { CredentialId = Guid.NewGuid(), ApplicationKey = "atlas", Environment = "Testing",
        Capabilities = [ManagementCustomerIdentityOperations.Info], ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1), Version = "v1" };
    public Task<ManagementApiCredentialEntity?> GetAsync(string app, string env, Guid id, CancellationToken ct = default)
        => Task.FromResult<ManagementApiCredentialEntity?>(app == Row.ApplicationKey && env == Row.Environment && id == Row.CredentialId ? Row : null);
    public Task<ManagementApiCredentialEntity> CreateAsync(ManagementApiCredentialEntity row, CancellationToken ct = default)
        { Row = row; return Task.FromResult(row); }
    public Task<ManagementApiCredentialEntity> ReplaceAsync(ManagementApiCredentialEntity row, string expectedVersion, CancellationToken ct = default)
        { if (expectedVersion != Row.Version) throw new ManagementCredentialConflictException(); Row = row; return Task.FromResult(row); }
}
internal sealed class FixtureCustomerAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        => Task.FromResult(Request.Headers.Authorization.ToString() == "Bearer customer"
            ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new("sub", "customer-user")], Scheme.Name)), Scheme.Name))
            : AuthenticateResult.NoResult());
}
